# Incremental Compilation for TypeScript

This is a drop-in replacement for the existing typescript msbuild task
that does incremental compilation, so that typescript builds run
fast(er).

To use this, move the targets and task file somewhere you can get them
from msbuild, and add the targets file to your project file, after the
standard typescript targets file. i.e., after the line that looks
something like this:

    <Import Project="$(VSToolsPath)\TypeScript\Microsoft.TypeScript.jsproj.targets" />

For an example of just such a project, look in the tests\testproject
directory.

You can also add the [TypeScript.Incremental nuget
package][https://www.nuget.org/packages/TypeScript.Incremental/] to your
project, which will do all the heavy lifting for you.

## How does it work?

That's easy! It works by maintaining a dependency graph between the
TypeScript files in your project. It's not very smart about it, though, and
it requires that you actually tell it about your dependencies by putting a
reference tag at the start of the file, e.g.:

    /// <reference path="other.ts" />

At some point, maybe it will learn how to understand

    module('other')

too. But for now, it only understands the simple one.

## License

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
