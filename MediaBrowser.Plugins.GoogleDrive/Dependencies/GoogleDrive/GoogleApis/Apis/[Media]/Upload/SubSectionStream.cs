using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Google.Apis.Upload
{
    internal class SubSectionStream : Stream
    {
        private Stream baseStream;
        private readonly long length;
        private long position;

        public SubSectionStream(Stream baseStream, long offset, long length)
        {
            if (baseStream == null)
            {
                throw new ArgumentNullException(nameof(baseStream));
            }

            if (!baseStream.CanRead)
            {
                throw new ArgumentException("can't read base stream");
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            this.baseStream = baseStream;
            this.length = length;

            if (baseStream.CanSeek)
            {
                baseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                // read it manually...
                const int BUFFER_SIZE = 512;
                byte[] buffer = new byte[BUFFER_SIZE];
                while (offset > 0)
                {
                    int read = baseStream.Read(buffer, 0, offset < BUFFER_SIZE ? (int)offset : BUFFER_SIZE);
                    offset -= read;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            this.CheckDisposed();

            long remaining = this.length - this.position;

            if (remaining <= 0)
            {
                return 0;
            }

            if (remaining < count)
            {
                count = (int)remaining;
            }

            int read = this.baseStream.Read(buffer, offset, count);
            this.position += read;

            return read;
        }

        private void CheckDisposed()
        {
            if (this.baseStream == null)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
        }

        public override long Length
        {
            get
            {
                this.CheckDisposed(); 
                return this.length;
            }
        }

        public override bool CanRead
        {
            get
            {
                this.CheckDisposed(); 
                return true;
            }
        }

        public override bool CanWrite
        {
            get
            {
                this.CheckDisposed(); 
                return false;
            }
        }

        public override bool CanSeek
        {
            get
            {
                this.CheckDisposed(); 
                return false;
            }
        }

        public override long Position
        {
            get
            {
                this.CheckDisposed();
                return this.position;
            }

            set
            {
                throw new NotSupportedException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                if (this.baseStream != null)
                {
                    ////try
                    ////{
                    ////    this.baseStream.Dispose();
                    ////}
                    ////catch { }

                    this.baseStream = null;
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
