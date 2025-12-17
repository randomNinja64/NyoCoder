// Guids.cs
// MUST match guids.h
using System;

namespace NyoCoder.NyoCoder_VSIX
{
    static class GuidList
    {
        public const string guidNyoCoder_VSIXPkgString = "c1a86c2a-28ac-4f5c-b57f-331712cfa0f3";
        public const string guidNyoCoder_VSIXCmdSetString = "d8bbe10d-2183-4406-831b-b33f4bc78792";
        public const string guidToolWindowPersistanceString = "5a58e5c5-1385-41dc-953e-a1b84efe50db";

        public static readonly Guid guidNyoCoder_VSIXCmdSet = new Guid(guidNyoCoder_VSIXCmdSetString);
    };
}