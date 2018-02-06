using SharpCompress.Compressors.Deflate;
using System.Collections.Generic;
using System.IO;

namespace Leayal.Closers.CMF
{
    class CMFReader : IReader
    {
        private long dataoffsetStart;
        private IEnumerator<CMFEntry> entryWalker;
        private CMFArchive sourceArchive;
        private Stream currentStream;

        internal CMFReader(CMFArchive archive)
        {
            this.sourceArchive = archive;
            this.dataoffsetStart = archive.dataoffsetStart;
            this.entryWalker = archive.Entries.GetEnumerator();
        }

        public CMFEntry Entry => this.entryWalker.Current;

        private bool _disposed;
        public void Dispose()
        {
            if (this._disposed) return;
            this._disposed = true;
            if (this.currentStream != null)
            {
                this.currentStream.Dispose();
                this.currentStream = null;
            }
            this.entryWalker.Dispose();
            this.Disposed?.Invoke(this, System.EventArgs.Empty);
        }

        internal event System.EventHandler Disposed;

        public bool MoveToNextEntry()
        {
            bool result = this.entryWalker.MoveNext();
            if (result && this.currentStream != null)
            {
                this.currentStream.Dispose();
                this.currentStream = null;
            }
            return result;
        }

        public Stream OpenEntryStream()
        {
            long entrydataoffset = this.Entry.dataoffset + this.dataoffsetStart;
            if (Entry.IsCompressed)
            {
                this.currentStream = new ZlibStream(new EntryStream(this.sourceArchive.BaseStream, entrydataoffset, Entry.CompressedSize, true), System.IO.Compression.CompressionMode.Decompress, false);
            }
            else
            {
                // Let's extract encrypted content as raw data, too.
                // Because I don't know how to decrypt.
                this.currentStream = new EntryStream(this.sourceArchive.BaseStream, entrydataoffset, Entry.UnpackedSize, true);
            }
            return this.currentStream;
        }

        public void WriteEntryTo(Stream outStream)
        {
            using (Stream entryStream = this.OpenEntryStream())
                entryStream.CopyTo(outStream, 4096);
            outStream.Flush();
        }

        public void WriteEntryTo(string filepath)
        {
            using (FileStream fs = File.Create(filepath))
                this.WriteEntryTo(fs);
        }
    }
}
