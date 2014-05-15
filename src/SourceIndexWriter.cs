/*
Copyright (C) 2014 John Doty

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
namespace TypeScript.Tasks
{
    using System;
    using System.IO;

    /// <summary>
    /// A helper object to write a source index, ala the source map v3 spec:
    /// https://docs.google.com/document/d/1U1RGAehQwRypUTovF1KRlpiOFze0b-_2gc6fAH0KY0k/edit
    /// </summary>
    public class SourceIndexWriter : IDisposable
    {        
        int lastLineOffset = -1;
        TextWriter writer;
        readonly char[] buffer = new char[4096];

        /// <summary>
        /// Constructs a new instance of the <see cref="SourceIndexWriter"/> class.
        /// </summary>
        /// <param name="writer">The text writer that receive the source index.</param>
        /// <param name="targetSourceFile">The source file being described.</param>
        public SourceIndexWriter(TextWriter writer, string targetSourceFile)
        {
            if (writer == null) { throw new ArgumentNullException("writer"); }
            this.writer = writer;
            this.writer.WriteLine(@"{{ ""version"": 3, ""file"": ""{0}"", ""sections"": [", targetSourceFile);
        }

        /// <summary>
        /// Adds a source map to the source index being built.
        /// </summary>
        /// <param name="lineOffset">The line offset of the source map in the target file.</param>
        /// <param name="columnOffset">The column offset of the source map in the target file.</param>
        /// <param name="mapReader">A text reader that can be used to read the source map.</param>
        /// <remarks>Source maps must be added in order, that is, each call to WriteMap must specify a larger 
        /// lineOffset.</remarks>
        public void WriteMap(int lineOffset, int columnOffset, TextReader mapReader)
        {
            if (this.writer == null) { throw new ObjectDisposedException("This writer has already been finished"); }
            if (mapReader == null) { throw new ArgumentNullException("mapReader"); }
            if (lineOffset <= this.lastLineOffset)
            {
                throw new InvalidOperationException(String.Format(
                    "Source maps must be written in order. The last map had a line offset of {0}, but the current " +
                    "one has an offset of {1}",
                    this.lastLineOffset,
                    lineOffset));
            }
            if (this.lastLineOffset >= 0)
            {
                // This is not the first map we've written, need to follow the last map with a comma.
                writer.WriteLine(",");
            }
            this.lastLineOffset = lineOffset;

            writer.WriteLine(@"{{ ""offset"": {{""line"": {0}, ""column"": {1}}}, ""map"":", lineOffset, columnOffset);
            int count;
            do
            {
                count = mapReader.ReadBlock(buffer, 0, buffer.Length);
                if (count > 0) { this.writer.Write(buffer, 0, count); }
            } while (count > 0);
            writer.Write(@"}");
        }
        
        /// <summary>
        /// Finishes the source index and releases all resources.
        /// </summary>
        /// <remarks>Unlike other implementations of Dispose, this one is mandatory. Don't skip it.</remarks>
        public void Dispose()
        {
            if (this.writer == null) { return; }

            // Map entries leave the last line hanging, so we can write a trailing comma, if necessary.
            if (this.lastLineOffset >= 0) { this.writer.WriteLine(); }
            this.writer.WriteLine("]}");
            this.writer.Flush();
            this.writer = null;
        }
    }
}
