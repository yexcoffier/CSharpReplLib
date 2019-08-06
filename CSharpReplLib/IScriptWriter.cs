using System;
using System.Collections.Generic;
using System.Text;

namespace CSharpReplLib
{
    public interface IScriptWriter : IDisposable
    {
        void Open(ScriptHandler scriptHandler);
    }
}
