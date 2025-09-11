using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MatriX.API.Models
{
    public class TorInfo
    {
        public int port { get; set; }

        public int countError { get; set; }

        [JsonIgnore]
        public TaskCompletionSource<bool> taskCompletionSource { get; set; }

        public HashSet<string> clientIps { get; set; } = new HashSet<string>();

        public UserData user { get; set; }

        [JsonIgnore]
        public Thread thread { get; set; }

        public DateTime lastActive { get; set; }

        #region activeStreams
        [JsonIgnore]
        public ConcurrentDictionary<string, DateTime> activeStreams = new ConcurrentDictionary<string, DateTime>();

        [JsonIgnore]
        public ConcurrentDictionary<string, DateTime> filteredActiveStreams
        {
            get
            {
                DateTime threshold = DateTime.Now.AddSeconds(-AppInit.groupSettings(user.group).rateLimiter.timeout);
                var filtered = new ConcurrentDictionary<string, DateTime>();
                foreach (var kvp in activeStreams)
                {
                    if (kvp.Value >= threshold)
                        filtered.TryAdd(kvp.Key, kvp.Value);
                    else
                        activeStreams.TryRemove(kvp.Key, out _);
                }

                return filtered;
            }
        }
        #endregion


        #region process
        [JsonIgnore]
        public Process process { get; set; }

        public string process_log { get; set; } = string.Empty;

        public string exception { get; set; }

        public event EventHandler processForExit;

        public void OnProcessForExit()
        {
            processForExit?.Invoke(this, null);
        }
        #endregion

        #region Dispose
        bool IsDispose;

        public void Dispose()
        {
            if (IsDispose)
                return;

            try
            {
                IsDispose = true;
                int _pid = process.Id;

                #region process
                try
                {
                    process.Kill(true);
                    process.Dispose();
                }
                catch { }
                #endregion

                #region Bash
                try
                {
                    Bash.Run($"kill -9 $(ps axu | egrep \"/sandbox/{user.id}$\" | grep -v grep | awk '{{print $2}}')");
                    Bash.Run($"kill -9 {_pid}");
                }
                catch { }
                #endregion

                clientIps.Clear();
                thread = null;
            }
            catch { }
        }
        #endregion
    }
}
