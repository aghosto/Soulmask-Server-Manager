using SoulmaskServerManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace SoulmaskServerManager
{
    public class Log
    {
        public enum LogType
        {
            WSServer,
            MainConsole,
            PlayerData
        }


        /// <summary>
        /// 备份服务器重启、崩溃日志
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public static bool WriteServerCrashLog(Server server)
        {
            if (server == null) return false;

            if (string.IsNullOrEmpty(server.Path)) return false;

            try
            {
                string crashLogDir = Path.Combine(server.Path, "CrashLog", DateTime.Today.ToString("yyyy-MM-dd"), DateTime.Now.ToString("HH-mm-ss"));
                Directory.CreateDirectory(crashLogDir);

                CopyFileIfExists(Path.Combine(server.Path, "WS", "Saved", "Logs", "WS.log"), Path.Combine(crashLogDir, "WS.log"));
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private static void CopyFileIfExists(string sourcePath, string destinationPath)
        {
            if (File.Exists(sourcePath))
            {
                try
                {
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    //ShowLogError($"复制文件失败：{sourcePath} → {destinationPath}，错误：{ex.Message}");
                }
            }
            else
            {
                //ShowLogWarning($"文件不存在，跳过复制：{sourcePath}");
            }
        }
    }
}
