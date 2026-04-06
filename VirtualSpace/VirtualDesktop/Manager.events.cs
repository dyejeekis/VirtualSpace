/* Copyright (C) 2021 Dylan Cheng (https://github.com/newlooper)

This file is part of VirtualSpace.

VirtualSpace is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

VirtualSpace is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with VirtualSpace. If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;
using Notification.Wpf;
using VirtualSpace.AppLogs;
using VirtualSpace.Config;
using VirtualSpace.Helpers;
using VirtualSpace.VirtualDesktop.Api;
using ConfigManager = VirtualSpace.Config.Manager;

namespace VirtualSpace.VirtualDesktop
{
    internal static partial class VirtualDesktopManager
    {
        public static void RegisterVirtualDesktopEvents()
        {
            DesktopManagerWrapper.DesktopCreatedEvent += () =>
            {
                if ( !IsBatchCreate ) UpdateMainView();
            };
            DesktopManagerWrapper.DesktopDeletedEvent += vdn => { UpdateMainView( vdn: vdn ); };
            DesktopManagerWrapper.DesktopChangedEvent += vdn =>
            {
                LastDesktopId = vdn.OldId;
                if ( MainWindow.IsShowing() )
                    UpdateVdwBackground();

                if ( ConfigManager.Configs.Cluster.NotificationOnVdChanged )
                {
                    CultureInfo.CurrentUICulture = new CultureInfo( ConfigManager.CurrentProfile.UI.Language );
                    Logger.Notify( new NotifyObject
                    {
                        Title = Agent.Langs.GetString( "Cluster.Notification.SVD.Current" ) + DesktopWrapper.DesktopNameFromGuid( vdn.NewId ),
                        Message = Agent.Langs.GetString( "Cluster.Notification.SVD.Last" ) + DesktopWrapper.DesktopNameFromGuid( vdn.OldId ),
                        Background = new SolidColorBrush( Colors.DarkSlateGray ),
                        Foreground = new SolidColorBrush( Colors.White ),
                        Type = NotificationType.Notification,
                        ExpTime = TimeSpan.FromSeconds( 3 )
                    } );
                }

                MainWindow.UpdateVDIndexOnTrayIcon( vdn.NewId );
            };

            DesktopManagerWrapper.RegisterVirtualDesktopEvents(
                () =>
                {
                    Logger.Event( $"Wallpaper Changed" );
                    Parallel.ForEach( GetAllVirtualDesktops(), ( vdw, _ ) => { vdw.UpdateWallpaper(); } );
                },
                ( guid, path ) =>
                {
                    var vdwList = GetAllVirtualDesktops();
                    var vd      = ( from vdw in vdwList where vdw.VdId == guid select vdw ).FirstOrDefault();
                    if ( vd is null ) return;
                    vd.UpdateWallpaper();
                    Logger.Event( $"Desktop[{vd.VdIndex.ToString()}] Wallpaper Changed: {path}" );
                }
            );

            DesktopWrapper.OnDesktopVisibleEvent += ( desktop, forceFocusForegroundWindow ) =>
            {
                if ( MainWindow.IsShowing() )
                {
                    desktop.MakeVisible();
                    return;
                }

                forceFocusForegroundWindow ??= Manager.Configs.Cluster.ForceFocusForegroundWindow;
                if ( (bool)forceFocusForegroundWindow )
                {
                    desktop.MakeVisible();
                    ForceFocusForegroundWindow();
                }
                else
                {
                    desktop.MakeVisible();
                }
            };
        }

        private const int FocusRetryMax      = 5;
        private const int FocusRetryDelayMs  = 50;

        private static void ForceFocusForegroundWindow()
        {
            // var modifierHeld = LowLevelKeyboardHook.IsKeyHold( Keys.Menu ) ||
            //                    LowLevelKeyboardHook.IsKeyHold( Keys.ControlKey ) ||
            //                    LowLevelKeyboardHook.IsKeyHold( Keys.ShiftKey ) ||
            //                    LowLevelKeyboardHook.IsKeyHold( Keys.LWin ) ||
            //                    LowLevelKeyboardHook.IsKeyHold( Keys.RWin );
            // Logger.Info( $"[Focus] Start. ModifierHeld={modifierHeld}, CurrentDesktop={DesktopWrapper.CurrentGuid}" );

            LowLevelKeyboardHook.ForceForegroundFocus();

            for ( var i = 0; i < FocusRetryMax; i++ )
            {
                var fgWnd = User32.GetForegroundWindow();
                if ( fgWnd == IntPtr.Zero )
                {
                    // Logger.Info( $"[Focus] Retry {i}: No foreground window" );
                    break;
                }

                try
                {
                    var fgDesktop = DesktopWrapper.GuidFromWindow( fgWnd );
                    // Logger.Info( $"[Focus] Retry {i}: fgWnd=0x{fgWnd.ToString( "X" )}, fgDesktop={fgDesktop}, current={DesktopWrapper.CurrentGuid}, match={fgDesktop == DesktopWrapper.CurrentGuid}" );
                    if ( fgDesktop == DesktopWrapper.CurrentGuid ) break;
                }
                catch
                {
                    // Logger.Info( $"[Focus] Retry {i}: GuidFromWindow threw: {ex.Message}" );
                }

                System.Threading.Thread.Sleep( FocusRetryDelayMs );

                // Find a window on the current desktop and focus it
                var targetWnd = FindWindowOnCurrentDesktop();
                if ( targetWnd != IntPtr.Zero )
                {
                    // Logger.Info( $"[Focus] Retry {i}: Found target 0x{targetWnd.ToString( "X" )} on current desktop, focusing" );
                    FocusWindow( targetWnd );
                }
                // else
                // {
                //     Logger.Info( $"[Focus] Retry {i}: No window found on current desktop" );
                // }
            }
        }

        private static IntPtr FindWindowOnCurrentDesktop()
        {
            var currentGuid = DesktopWrapper.CurrentGuid;
            var result      = IntPtr.Zero;

            User32.EnumWindows( ( hWnd, _ ) =>
            {
                if ( !User32.IsWindowVisible( hWnd ) ) return true;

                var title = new System.Text.StringBuilder( Const.WindowTitleMaxLength );
                User32.GetWindowText( hWnd, title, Const.WindowTitleMaxLength );
                if ( title.Length == 0 ) return true;

                try
                {
                    var guid = DesktopWrapper.GuidFromWindow( hWnd );
                    if ( guid == currentGuid )
                    {
                        result = hWnd;
                        return false; // stop enumeration
                    }
                }
                catch
                {
                    // skip
                }

                return true;
            }, 0 );

            return result;
        }

        private static void FocusWindow( IntPtr hWnd )
        {
            var curThreadId = User32.GetCurrentThreadId();
            var fgThreadId  = (uint)User32.GetWindowThreadProcessId( hWnd, out _ );

            User32.SystemParametersInfo( User32.SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0 );

            if ( curThreadId != fgThreadId )
                User32.AttachThreadInput( curThreadId, fgThreadId, true );

            User32.SetForegroundWindow( hWnd );
            User32.BringWindowToTop( hWnd );
            User32.SwitchToThisWindow( hWnd, true );

            if ( curThreadId != fgThreadId )
                User32.AttachThreadInput( curThreadId, fgThreadId, false );
        }
    }
}