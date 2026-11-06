using System;
using System.Runtime.CompilerServices;
using TownOfHostL.Core;

namespace TownOfHostL.Modules
{
    class LogHandler : ILogHandler
    {
        public string Tag { get; }
        public LogHandler(string tag)
        {
            Tag = tag;
        }

        public void Info(string text)
            => Logger.Info(text, Tag, true);
        // 補間文字列で呼んだ時は、タグが無効なら文字列を組み立てない
        public void Info([InterpolatedStringHandlerArgument("")] ref LogText text)
        {
            if (text.Enabled) Logger.Info(text.ToStringAndClear(), Tag, true);
        }
        public void Warn(string text)
            => Logger.Warn(text, Tag, true);
        public void Warn([InterpolatedStringHandlerArgument("")] ref LogText text)
        {
            if (text.Enabled) Logger.Warn(text.ToStringAndClear(), Tag, true);
        }
        public void Error(string text)
            => Logger.Error(text, Tag, true);
        public void Fatal(string text)
            => Logger.Fatal(text, Tag, true);
        public void Msg(string text)
            => Logger.Msg(text, Tag, true);
        public void Exception(Exception ex)
            => Logger.Exception(ex, Tag);
    }
}