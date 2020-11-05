using System;
using System.IO;

namespace CASCLib
{
    public class SubStream : Stream
    {
        private Stream _baseStream;
        private long _offset, _length, _position = 0;

        public SubStream(Stream baseStream, long offset, long length)
        {
            if (length < 1) throw new ArgumentException("Length must be greater than zero.");

            this._baseStream = baseStream;
            this._offset = offset;
            this._length = length;

            baseStream.Seek(offset, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            long remaining = _length - _position;
            if (remaining <= 0) return 0;
            if (remaining < count) count = (int)remaining;
            int read = _baseStream.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        private void CheckDisposed()
        {
            if (_baseStream == null) throw new ObjectDisposedException(GetType().Name);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long pos = _position;

            if (origin == SeekOrigin.Begin)
                pos = offset;
            else if (origin == SeekOrigin.End)
                pos = _length + offset;
            else if (origin == SeekOrigin.Current)
                pos += offset;

            if (pos < 0)
                pos = 0;
            else if (pos >= _length)
                pos = _length - 1;

            _position = _baseStream.Seek(this._offset + pos, SeekOrigin.Begin) - this._offset;

            return pos;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position { get => _position; set { _position = this.Seek(value, SeekOrigin.Begin); } }

        public override void Flush() => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    }
}
