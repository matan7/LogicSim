using System;

namespace Core.Command
{
    public interface ICommand : IDisposable
    {
        void Execute();
        void Undo();
    }
}