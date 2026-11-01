using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Auros.Ram.App.Services;

/// <summary>
/// Copia una imagen al portapapeles de Windows en dos formatos a la vez: "PNG" (el que prefieren PowerPoint y Word,
/// conserva la nitidez) y CF_DIB (mapa de bits estándar, para cualquier otro programa). Avalonia 11 no copia imágenes.
/// </summary>
public static partial class ImageClipboard
{
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;

    /// <returns>false si no se pudo (otro programa tiene abierto el portapapeles, o no es Windows).</returns>
    public static bool SetImage(byte[] png, byte[] dib)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // El portapapeles necesita una ventana dueña: con null, Windows rechaza los datos.
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (owner == IntPtr.Zero)
        {
            return false;
        }

        // Otro programa puede tenerlo abierto un instante: se reintenta.
        var opened = false;
        for (var i = 0; i < 10 && !(opened = OpenClipboard(owner)); i++)
        {
            Thread.Sleep(30);
        }

        if (!opened)
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var pngFormat = RegisterClipboardFormatW("PNG");
            return Put(pngFormat, png) & Put(CfDib, dib);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Encabezado BITMAPINFOHEADER de 32 bits más las filas de abajo hacia arriba (como espera CF_DIB).</summary>
    public static byte[] ToDib(byte[] bgra, int width, int height)
    {
        const int header = 40;
        var stride = width * 4;
        var dib = new byte[header + (stride * height)];
        BitConverter.TryWriteBytes(dib.AsSpan(0), header);       // biSize
        BitConverter.TryWriteBytes(dib.AsSpan(4), width);        // biWidth
        BitConverter.TryWriteBytes(dib.AsSpan(8), height);       // biHeight (positivo: de abajo hacia arriba)
        BitConverter.TryWriteBytes(dib.AsSpan(12), (short)1);    // biPlanes
        BitConverter.TryWriteBytes(dib.AsSpan(14), (short)32);   // biBitCount
        BitConverter.TryWriteBytes(dib.AsSpan(20), stride * height); // biSizeImage (biCompression = BI_RGB = 0)
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(bgra, y * stride, dib, header + ((height - 1 - y) * stride), stride);
        }

        return dib;
    }

    private static bool Put(uint format, byte[] data)
    {
        var handle = GlobalAlloc(GmemMoveable, (nuint)data.Length);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var target = GlobalLock(handle);
        Marshal.Copy(data, 0, target, data.Length);
        GlobalUnlock(handle);

        // Si Windows la acepta, la memoria pasa a ser del portapapeles; si no, se libera.
        if (SetClipboardData(format, handle) != IntPtr.Zero)
        {
            return true;
        }

        GlobalFree(handle);
        return false;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr data);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr handle);
}
