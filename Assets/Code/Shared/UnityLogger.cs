using UnityEngine;
using ILogger = LiteEntitySystem.ILogger;

namespace Code.Shared
{
    public class UnityLogger : ILogger
    {
        public void Log(string log)
        {
            Debug.Log(log);
        }

        public void LogError(string log)
        {
            Debug.LogError(log);
        }

        public void LogWarning(string log)
        {
            Debug.LogWarning(log);
        }
    }
}