using System;
using System.Collections.Immutable;
using System.Diagnostics;
using IslandTaskbar.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Copies optional shell icons; all native objects stay within the owning apartment (D-014).</summary>
    internal static class ApplicationIconLoader
    {
        internal static unsafe ApplicationIcon? Load(string path)
        {
            SHFILEINFOW info = default;
            HDC dc = default;
            HBITMAP bitmap = default;
            HGDIOBJ previous = default;
            try
            {
                if (PInvoke.SHGetFileInfo(path, 0, ref info, SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_LARGEICON) == 0 || info.hIcon == default)
                    return null;
                dc = PInvoke.CreateCompatibleDC(default);
                if (dc == default) return null;
                const int size = 32;
                BITMAPINFO format = new() { bmiHeader = new BITMAPINFOHEADER
                { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 } };
                void* pixels = null;
                bitmap = PInvoke.CreateDIBSection(dc, &format, DIB_USAGE.DIB_RGB_COLORS, &pixels, default, 0);
                if (bitmap == default || pixels == null) return null;
                previous = PInvoke.SelectObject(dc, (HGDIOBJ)bitmap);
                if (previous == default || (nint)previous == -1) return null;
                Span<byte> surface = new(pixels, size * size * 4);
                surface.Clear();
                if (!PInvoke.DrawIconEx(dc, 0, 0, info.hIcon, size, size, 0, default, DI_FLAGS.DI_NORMAL)) return null;
                byte[] black = surface.ToArray();
                surface.Fill(255);
                if (!PInvoke.DrawIconEx(dc, 0, 0, info.hIcon, size, size, 0, default, DI_FLAGS.DI_NORMAL)) return null;
                for (int index = 0; index < black.Length; index += 4)
                {
                    int transparency = Math.Max(surface[index] - black[index],
                        Math.Max(surface[index + 1] - black[index + 1], surface[index + 2] - black[index + 2]));
                    black[index + 3] = (byte)Math.Clamp(255 - transparency, 0, 255);
                }
                return new(size, size, ImmutableArray.CreateRange(black));
            }
            finally
            {
                if (previous != default && (nint)previous != -1) PInvoke.SelectObject(dc, previous);
                if (bitmap != default && !PInvoke.DeleteObject((HGDIOBJ)bitmap)) Debug.WriteLine("Application icon bitmap cleanup failed.");
                if (dc != default && !PInvoke.DeleteDC(dc)) Debug.WriteLine("Application icon DC cleanup failed.");
                if (info.hIcon != default && !PInvoke.DestroyIcon(info.hIcon)) Debug.WriteLine("Application icon handle cleanup failed.");
            }
        }
    }
}
