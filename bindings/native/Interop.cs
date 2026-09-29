using System.Runtime.InteropServices;
using System.Text;

namespace Frisket.Native
{
    /// <summary>
    /// What every export needs and none of them should repeat: turning a pointer into a
    /// string, a string into a buffer the caller owns, and an exception into a code.
    /// </summary>
    internal static class Interop
    {
        /// <summary>The last thing that went wrong, per thread.</summary>
        [ThreadStatic] private static string? _error;

        /// <summary>
        /// Kept alive for as long as the caller might read it. A message handed out as a
        /// pointer and then collected is a crash in somebody else's language, days later
        /// and nowhere near here.
        /// </summary>
        [ThreadStatic] private static IntPtr _errorText;

        internal const int Ok = 0;
        internal const int ErrArgument = 1;
        internal const int ErrFailed = 2;

        /// <summary>
        /// The caller asked for this to stop, and it did.
        ///
        /// Its own code rather than a failure, because the two are answered differently:
        /// a caller retries after a failure and does not retry after a cancellation.
        /// Folded together, the first timeout on a busy server becomes a storm.
        /// </summary>
        internal const int Cancelled = 3;

        /// <summary>
        /// Runs <paramref name="work"/> and turns whatever it throws into a status code.
        /// Nothing may propagate: an exception crossing the boundary tears the process
        /// down with no message worth reading.
        /// </summary>
        internal static int Guarded(Func<int> work)
        {
            try
            {
                _error = null;
                return work();
            }
            catch (OperationCanceledException)
            {
                SetError("The operation was cancelled.");
                return Cancelled;
            }
            catch (Exception e)
            {
                SetError(e.Message);
                return ErrFailed;
            }
        }

        internal static int Fail(string message)
        {
            SetError(message);
            return ErrArgument;
        }

        private static void SetError(string message)
        {
            _error = message;

            if (_errorText != IntPtr.Zero)
                Marshal.FreeHGlobal(_errorText);

            _errorText = Utf8(message);
        }

        internal static IntPtr LastError() => _errorText;

        /// <summary>A null-terminated UTF-8 copy, on the unmanaged heap.</summary>
        internal static IntPtr Utf8(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            IntPtr memory = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            Marshal.WriteByte(memory, bytes.Length, 0);
            return memory;
        }

        /// <summary>
        /// Reads a null-terminated UTF-8 string. Null and empty are told apart: a caller
        /// passing no data at all is not the same as one passing an empty section.
        /// </summary>
        internal static string? Read(IntPtr text)
            => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);
    }

    /// <summary>
    /// A block of bytes the library made and the caller reads. Owned here and freed by
    /// <c>fk_buffer_free</c>: handing back a pointer into managed memory would mean the
    /// collector is free to move it while C is reading it.
    /// </summary>
    internal sealed class Buffer
    {
        internal IntPtr Data { get; private set; }
        internal int Size { get; }

        internal Buffer(byte[] bytes)
        {
            Size = bytes.Length;

            // One extra zero byte, always. It costs nothing and it means a text result
            // can be used as a C string without the caller copying it first.
            Data = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, Data, bytes.Length);
            Marshal.WriteByte(Data, bytes.Length, 0);
        }

        internal Buffer(string text) : this(Encoding.UTF8.GetBytes(text)) { }

        internal void Release()
        {
            if (Data == IntPtr.Zero)
                return;

            Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Managed objects behind the opaque pointers the C side holds. A handle is a
    /// <see cref="GCHandle"/>, so the object stays put and stays alive until the caller
    /// says otherwise - which is the only lifetime rule the boundary has.
    ///
    /// Every handle this hands out is also written down, and nothing is dereferenced
    /// until the pointer has been found in that list. <see cref="Interop.Guarded"/>
    /// turns exceptions into codes, but <c>GCHandle.FromIntPtr</c> on a value that was
    /// never a handle does not raise one: it tears the process down where no catch can
    /// reach, and a caller who passed the wrong variable gets no message at all.
    ///
    /// A tag written beside the object would not do, however tempting - reading the tag
    /// is itself a dereference, and reading from an address that was never ours is the
    /// very thing being guarded against. The check has to happen without touching the
    /// pointer, which means a list kept on this side.
    ///
    /// Locked, because a caller may well render on several threads, and because the
    /// list is the thing standing between a mistake and a dead process. The cost is a
    /// hash lookup against work measured in milliseconds.
    /// </summary>
    internal static class Handles
    {
        private static readonly HashSet<IntPtr> _issued = new();
        private static readonly object _gate = new();

        internal static IntPtr Wrap(object value)
        {
            IntPtr handle = GCHandle.ToIntPtr(GCHandle.Alloc(value));
            lock (_gate)
                _issued.Add(handle);
            return handle;
        }

        /// <summary>
        /// The object behind a handle, or null when the pointer is not one this library
        /// issued - which every export already reports as a bad argument.
        /// </summary>
        internal static T? Unwrap<T>(IntPtr handle) where T : class
        {
            if (handle == IntPtr.Zero)
                return null;

            lock (_gate)
            {
                if (!_issued.Contains(handle))
                    return null;
            }

            return GCHandle.FromIntPtr(handle).Target as T;
        }

        internal static void Release(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;

            // Taken out of the list before anything else, so a second free finds nothing
            // and does nothing rather than freeing a handle twice.
            lock (_gate)
            {
                if (!_issued.Remove(handle))
                    return;
            }

            GCHandle held = GCHandle.FromIntPtr(handle);
            if (held.Target is Buffer buffer)
                buffer.Release();

            held.Free();
        }
    }
}
