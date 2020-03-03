﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Timers;

namespace PDFtoPrinter
{
    public class CleanUpFilesPDFtoPrinterWrapper : IPDFtoPrinterWrapper
    {
        private static readonly IDictionary<string, ConcurrentQueue<string>> PrintingQueues =
            new ConcurrentDictionary<string, ConcurrentQueue<string>>();
        private static readonly object Locker = new object();
        private static readonly Timer CleanupTimer = new Timer(10 * 1000)
        {
            AutoReset = true,
            Enabled = true
        };
        private static bool DeletingInProgress = false;

        private readonly IPDFtoPrinterWrapper inner;

        static CleanUpFilesPDFtoPrinterWrapper()
        {
            CleanupTimer.Elapsed += CleanupTimerElapsed;
            CleanupTimer.Enabled = true;
        }

        public CleanUpFilesPDFtoPrinterWrapper(IPDFtoPrinterWrapper inner)
        {
            this.inner = inner;
        }

        public async Task Print(string filePath, string printerName, TimeSpan? timeout = null)
        {
            await this.inner.Print(filePath, printerName, timeout);
            this.EnqueuePrintingFile(printerName, filePath);
        }

        private void EnqueuePrintingFile(string printerName, string filePath)
        {
            ConcurrentQueue<string> queue = this.GetQueue(printerName);
            queue.Enqueue(filePath);
        }

        private ConcurrentQueue<string> GetQueue(string printerName)
        {
            if (!PrintingQueues.ContainsKey(printerName))
            {
                lock (Locker)
                {
                    if (!PrintingQueues.ContainsKey(printerName))
                    {
                        PrintingQueues.Add(printerName, new ConcurrentQueue<string>());
                    }
                }
            }

            return PrintingQueues[printerName];
        }

        private static void CleanupTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (DeletingInProgress)
            {
                return;
            }

            DeletingInProgress = true;
            CleanupPrintedFiles(PrintingQueues);
            DeletingInProgress = false;
        }

        private static void CleanupPrintedFiles(
            IDictionary<string, ConcurrentQueue<string>> printingQueues)
        {
            using (var printServer = new PrintServer())
            {
                foreach (KeyValuePair<string, ConcurrentQueue<string>> queue
                    in printingQueues)
                {
                    DeletePrintedFiles(printServer, queue.Key, queue.Value);
                }
            }
        }

        private static void DeletePrintedFiles(
            PrintServer printServer,
            string queueName,
            ConcurrentQueue<string> files)
        {
            using (PrintQueue printerQueue = printServer.GetPrintQueue(queueName))
            {
                DeletePrintedFiles(files, printerQueue);
            }
        }

        private static void DeletePrintedFiles(
            ConcurrentQueue<string> files,
            PrintQueue printerQueue)
        {
            var printingItems = new HashSet<string>(printerQueue
                .GetPrintJobInfoCollection()
                .Select(x => x.Name.ToUpper()));
            while (!files.IsEmpty)
            {
                files.TryPeek(out string currentFile);
                if (printingItems.Contains(Path.GetFileName(currentFile).ToUpper()))
                {
                    break;
                }

                files.TryDequeue(out string dequeuedFile);
                try
                {
                    File.Delete(dequeuedFile);
                }
                catch { }
            }
        }
    }
}
