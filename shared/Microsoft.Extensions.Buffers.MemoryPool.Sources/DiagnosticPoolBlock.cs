// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace System.Buffers
{
    /// <summary>
    /// Block tracking object used by the byte buffer memory pool. A slab is a large allocation which is divided into smaller blocks. The
    /// individual blocks are then treated as independent array segments.
    /// </summary>
    internal sealed class DiagnosticPoolBlock : MemoryManager<byte>
    {
        /// <summary>
        /// Back-reference to the memory pool which this block was allocated from. It may only be returned to this pool.
        /// </summary>
        private readonly DiagnosticMemoryPool _pool;

        private readonly IMemoryOwner<byte> _memoryOwner;
        private MemoryHandle? _memoryHandle;
        private Memory<byte> _memory;

        private readonly object _syncObj = new object();
        private bool _isDisposed;
        private int _pinCount;


        /// <summary>
        /// This object cannot be instantiated outside of the static Create method
        /// </summary>
        internal DiagnosticPoolBlock(DiagnosticMemoryPool pool, IMemoryOwner<byte> memoryOwner)
        {
            _pool = pool;
            _memoryOwner = memoryOwner;
            _memory = memoryOwner.Memory;
        }

        public override Memory<byte> Memory
        {
            get
            {
                try
                {
                    lock (_syncObj)
                    {
                        if (_isDisposed)
                        {
                            MemoryPoolThrowHelper.ThrowObjectDisposedException(MemoryPoolThrowHelper.ExceptionArgument.MemoryPoolBlock);
                        }

                        if (_pool.IsDisposed)
                        {
                            MemoryPoolThrowHelper.ThrowInvalidOperationException_BlockIsBackedByDisposedSlab();
                        }

                        return CreateMemory(_memory.Length);
                    }
                }
                catch (Exception exception)
                {
                    _pool.ReportException(exception);
                    throw;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                lock (_syncObj)
                {
                    if (Volatile.Read(ref _pinCount) > 0)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_ReturningPinnedBlock();
                    }

                    if (_isDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_DoubleDispose();
                    }

                    _pool.Return(this);

                    _memoryOwner.Dispose();

                    if (_pool.IsDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_BlockReturnedToDisposedPool();
                    }

                    _isDisposed = true;
                }
            }
            catch (Exception exception)
            {
                _pool.ReportException(exception);
                throw;
            }
        }

        public override Span<byte> GetSpan()
        {
            try
            {
                lock (_syncObj)
                {
                    if (_isDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowObjectDisposedException(MemoryPoolThrowHelper.ExceptionArgument.MemoryPoolBlock);
                    }

                    if (_pool.IsDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_BlockIsBackedByDisposedSlab();
                    }

                    return _memory.Span;
                }
            }
            catch (Exception exception)
            {
                _pool.ReportException(exception);
                throw;
            }
        }

        public override MemoryHandle Pin(int byteOffset = 0)
        {
            try
            {
                lock (_syncObj)
                {
                    if (_isDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowObjectDisposedException(MemoryPoolThrowHelper.ExceptionArgument.MemoryPoolBlock);
                    }

                    if (_pool.IsDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_BlockIsBackedByDisposedSlab();
                    }

                    if (byteOffset < 0 || byteOffset > _memory.Length)
                    {
                        MemoryPoolThrowHelper.ThrowArgumentOutOfRangeException(_memory.Length, byteOffset);
                    }

                    _pinCount++;

                    _memoryHandle = _memoryHandle ?? _memory.Pin();

                    unsafe
                    {
                        return new MemoryHandle(_memoryHandle.Value.Pointer, default, this);
                    }
                }
            }
            catch (Exception exception)
            {
                _pool.ReportException(exception);
                throw;
            }
        }

        protected override bool TryGetArray(out ArraySegment<byte> segment)
        {
            try
            {
                lock (_syncObj)
                {
                    if (_isDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowObjectDisposedException(MemoryPoolThrowHelper.ExceptionArgument.MemoryPoolBlock);
                    }

                    if (_pool.IsDisposed)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_BlockIsBackedByDisposedSlab();
                    }

                    return MemoryMarshal.TryGetArray(_memory, out segment);
                }
            }
            catch (Exception exception)
            {
                _pool.ReportException(exception);
                throw;
            }
        }

        public override void Unpin()
        {
            try
            {
                lock (_syncObj)
                {
                    if (_pinCount == 0)
                    {
                        MemoryPoolThrowHelper.ThrowInvalidOperationException_PinCountZero();
                    }

                    _pinCount--;

                    if (_pinCount == 0)
                    {
                        Debug.Assert(_memoryHandle.HasValue);
                        _memoryHandle.Value.Dispose();
                        _memoryHandle = null;
                    }
                }
            }
            catch (Exception exception)
            {
                _pool.ReportException(exception);
                throw;
            }
        }

#if BLOCK_LEASE_TRACKING
        public bool IsLeased { get; set; }
        public string Leaser { get; set; }

        public void Lease()
        {
            Leaser = Environment.StackTrace;
            IsLeased = true;
        }
#endif
    }
}
