using SharpCompress.Compressors.Deflate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Leayal.Closers.CMF
{
    class CMFEditor : IEditor
    {
        private Dictionary<CMFEntry, object> datadictionary;
        private CMFArchive myArchive;

        internal CMFEditor(CMFArchive archive, CompressionLevel compression)
        {
            this.myArchive = archive;
            this.CompressionLevel = compression;
            this.datadictionary = new Dictionary<CMFEntry, object>();
        }

        public CompressionLevel CompressionLevel { get; }

        private bool SetEntryData(CMFEntry entry, object data)
        {
            if (entry == null || this.myArchive.Entries.IndexOf(entry) == -1)
                return false;
            else
            {
                this.datadictionary[entry] = data;
                return true;
            }
        }

        public bool SetData(int entryIndex, byte[] data)
        {
            if ((0 <= entryIndex) && (entryIndex < this.myArchive.Entries.Count))
                return this.SetData(this.myArchive[entryIndex], data);
            else
                return false;
        }

        public bool SetData(string entryPath, byte[] data)
        {
            return this.SetData(this.myArchive[entryPath], data);
        }

        public bool SetData(CMFEntry entry, byte[] data)
        {
            return this.SetEntryData(entry, data);
        }

        public bool SetDataSource(int entryIndex, Stream data)
        {
            if ((0 <= entryIndex) && (entryIndex < this.myArchive.Entries.Count))
                return this.SetDataSource(this.myArchive[entryIndex], data);
            else
                return false;
        }

        public bool SetDataSource(string entryPath, Stream data)
        {
            return this.SetDataSource(this.myArchive[entryPath], data);
        }

        public bool SetDataSource(CMFEntry entry, Stream data)
        {
            if (!data.CanRead)
                throw new InvalidOperationException("The data's stream should be readable.");
            return this.SetEntryData(entry, data);
        }

        public bool SetString(int entryIndex, string data)
        {
            if ((0 <= entryIndex) && (entryIndex < this.myArchive.Entries.Count))
                return this.SetString(this.myArchive[entryIndex], data);
            else
                return false;
        }

        public bool SetString(string entryPath, string data)
        {
            return this.SetString(this.myArchive[entryPath], data);
        }

        public bool SetString(CMFEntry entry, string data)
        {
            return this.SetString(entry, data, Encoding.UTF8);
        }

        public bool SetString(int entryIndex, string data, Encoding encoding)
        {
            if ((0 <= entryIndex) && (entryIndex < this.myArchive.Entries.Count))
                return this.SetString(this.myArchive[entryIndex], data, encoding);
            else
                return false;
        }

        public bool SetString(string entryPath, string data, Encoding encoding)
        {
            return this.SetString(this.myArchive[entryPath], data, encoding);
        }

        public bool SetString(CMFEntry entry, string data, Encoding encoding)
        {
            return this.SetEntryData(entry, encoding.GetBytes(data));
        }

        public void Save()
        {
            if (!this.myArchive.BaseStream.CanWrite)
                throw new InvalidOperationException("The archive is opened with read-only stream. Use SaveAs() instead or re-open the file with read/write access.");

            long oldpos, newpos, newlength, paddingsize;
            byte[] buffer = new byte[4096];

            foreach (var entrydata in datadictionary)
            {
                if (this.myArchive.Entries.IndexOf(entrydata.Key) > -1)
                {
                    this.myArchive.BaseStream.Seek(entrydata.Key.dataoffset, SeekOrigin.Begin);
                    if (entrydata.Value is Stream)
                    {
                        oldpos = this.myArchive.BaseStream.Position;
                        Stream stream = (Stream)entrydata.Value;
                        if (entrydata.Key.IsCompressed)
                            using (ZlibStream zlibstream = new ZlibStream(this.myArchive.BaseStream, System.IO.Compression.CompressionMode.Compress, this.CompressionLevel, true, Encoding.UTF8))
                                CopyStream(stream, zlibstream, ref buffer);
                        else
                            CopyStream(stream, this.myArchive.BaseStream, ref buffer);
                        newpos = this.myArchive.BaseStream.Position;
                        newlength = newpos - oldpos;
                        if (newlength > entrydata.Key.CompressedSize)
                            throw new InvalidDataException($"The new data of '{entrydata.Key.FileName}' size is bigger than the original one.");
                        else if (newlength < entrydata.Key.CompressedSize)
                        {
                            paddingsize = entrydata.Key.CompressedSize - newlength;
                            for (int paddingCount = 0; paddingCount < paddingsize; paddingCount++)
                                this.myArchive.BaseStream.WriteByte(0);
                        }
                    }
                    else if (entrydata.Value is byte[])
                    {
                        oldpos = this.myArchive.BaseStream.Position;
                        byte[] dataArray = (byte[])entrydata.Value;
                        if (entrydata.Key.IsCompressed)
                            using (ZlibStream zlibstream = new ZlibStream(this.myArchive.BaseStream, System.IO.Compression.CompressionMode.Compress, this.CompressionLevel, true, Encoding.UTF8))
                                zlibstream.Write(dataArray, 0, dataArray.Length);
                        else
                            this.myArchive.BaseStream.Write(dataArray, 0, dataArray.Length);
                        newpos = this.myArchive.BaseStream.Position;
                        newlength = newpos - oldpos;
                        if (newlength > entrydata.Key.CompressedSize)
                            throw new InvalidDataException($"The new data of '{entrydata.Key.FileName}' size is bigger than the original one.");
                        else if (newlength < entrydata.Key.CompressedSize)
                        {
                            paddingsize = entrydata.Key.CompressedSize - newlength;
                            for (int paddingCount = 0; paddingCount < paddingsize; paddingCount++)
                                this.myArchive.BaseStream.WriteByte(0);
                        }
                    }
                }
            }

            this.myArchive.BaseStream.Flush();
        }

        public void WriteTo(string filepath)
        {
            FileStream tfs = this.myArchive.BaseStream as FileStream;
            if (tfs != null || string.Equals(tfs.Name, Path.GetFullPath(filepath), StringComparison.OrdinalIgnoreCase))
                this.Save();
            else
            {
                using (Stream fs = File.Create(filepath))
                    this.WriteTo(fs);
            }
        }

        public void WriteTo(Stream outStream)
        {
            if (datadictionary.Count == 0)
                return;

            // Copy header
            using (EntryStream stream = new EntryStream(this.myArchive.BaseStream, 0, this.myArchive.dataoffsetStart, true))
                stream.CopyTo(outStream);

            // Write entry's content
            CMFEntry entry;
            object data;
            long oldpos, newpos, newlength, paddingsize;

            byte[] buffer = new byte[4096];

            for (int i = 0; i < this.myArchive.Entries.Count; i++)
            {
                entry = this.myArchive.Entries[i];
                if (this.datadictionary.ContainsKey(entry))
                {
                    data = this.datadictionary[entry];
                    if (data is Stream)
                    {
                        oldpos = outStream.Position;
                        Stream stream = (Stream)data;
                        if (entry.IsCompressed)
                            using (ZlibStream zlibstream = new ZlibStream(outStream, System.IO.Compression.CompressionMode.Compress, this.CompressionLevel, true, Encoding.UTF8))
                                CopyStream(stream, zlibstream, ref buffer);
                        else
                            CopyStream(stream, outStream, ref buffer);
                        newpos = outStream.Position;
                        newlength = newpos - oldpos;
                        if (newlength > entry.CompressedSize)
                            throw new InvalidDataException($"The new data of '{entry.FileName}' size is bigger than the original one.");
                        else if (newlength < entry.CompressedSize)
                        {
                            paddingsize = entry.CompressedSize - newlength;
                            for (int paddingCount = 0; paddingCount < paddingsize; paddingCount++)
                                outStream.WriteByte(0);
                        }
                    }
                    else if (data is byte[])
                    {
                        oldpos = outStream.Position;
                        byte[] dataArray = (byte[])data;
                        if (entry.IsCompressed)
                            using (ZlibStream zlibstream = new ZlibStream(outStream, System.IO.Compression.CompressionMode.Compress, this.CompressionLevel, true, Encoding.UTF8))
                                zlibstream.Write(dataArray, 0, dataArray.Length);
                        else
                            outStream.Write(dataArray, 0, dataArray.Length);
                        newpos = outStream.Position;
                        newlength = newpos - oldpos;
                        if (newlength > entry.CompressedSize)
                            throw new InvalidDataException($"The new data of '{entry.FileName}' size is bigger than the original one.");
                        else if (newlength < entry.CompressedSize)
                        {
                            paddingsize = entry.CompressedSize - newlength;
                            for (int paddingCount = 0; paddingCount < paddingsize; paddingCount++)
                                outStream.WriteByte(0);
                        }
                    }
                }
                else
                {
                    using (EntryStream stream = new EntryStream(this.myArchive.BaseStream, entry.dataoffset, entry.CompressedSize, true))
                        CopyStream(stream, outStream, ref buffer);
                }
            }

            outStream.Flush();
        }

        private static void CopyStream(Stream inStream, Stream outStrema, ref byte[] buffer)
        {
            int readCount = inStream.Read(buffer, 0, buffer.Length);
            while (readCount>0)
            {
                outStrema.Write(buffer, 0, readCount);
                readCount = inStream.Read(buffer, 0, buffer.Length);
            }
        }

        private bool _disposed;
        public void Dispose()
        {
            if (this._disposed) return;
            this._disposed = true;
            this.datadictionary.Clear();
            this.datadictionary = null;
            this.Disposed?.Invoke(this, EventArgs.Empty);
        }

        internal event EventHandler Disposed;
    }
}
