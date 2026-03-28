using TerraFX.Interop.Windows;

namespace KernSmith.Rasterizers.DirectWrite.TerraFX;

/// <summary>
/// Minimal COM smart pointer for safe Release() of TerraFX COM objects.
/// </summary>
internal unsafe struct ComPtr<T> : IDisposable where T : unmanaged
{
    private T* _ptr;

    public ComPtr(T* ptr)
    {
        _ptr = ptr;
    }

    public T* Get => _ptr;

    public T** GetAddressOf()
    {
        fixed (T** p = &_ptr)
            return p;
    }

    public readonly bool IsNull => _ptr == null;

    /// <summary>
    /// Releases the current pointer and returns the address for receiving a new one.
    /// </summary>
    public T** ReleaseAndGetAddressOf()
    {
        Release();
        fixed (T** p = &_ptr)
            return p;
    }

    public void Release()
    {
        if (_ptr != null)
        {
            ((IUnknown*)_ptr)->Release();
            _ptr = null;
        }
    }

    public void Dispose()
    {
        Release();
    }

    public static implicit operator T*(ComPtr<T> ptr) => ptr._ptr;
    public static implicit operator bool(ComPtr<T> ptr) => ptr._ptr != null;
}
