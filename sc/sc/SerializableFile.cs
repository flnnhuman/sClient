using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace sc {
	// TODO: заменить на sqlite мб
	public abstract class SerializableFile : IDisposable {
		private readonly SemaphoreSlim FileSemaphore = new SemaphoreSlim(1, 1);

		private Logger _logger;

		private bool ReadOnly;
		private bool SavingScheduled;

		protected string FilePath { private get; set; }

		public virtual void Dispose() => FileSemaphore.Dispose();

		internal async Task MakeReadOnly() {
			if (ReadOnly) {
				return;
			}

			await FileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (ReadOnly) {
					return;
				}

				ReadOnly = true;
			} finally {
				FileSemaphore.Release();
			}
		}

		public async Task Save() {
			if (ReadOnly || string.IsNullOrEmpty(FilePath)) {
				return;
			}

			lock (FileSemaphore) {
				if (SavingScheduled) {
					return;
				}

				SavingScheduled = true;
			}

			await FileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (FileSemaphore) {
					SavingScheduled = false;
				}

				if (ReadOnly) {
					return;
				}

				string json = JsonConvert.SerializeObject(this, /*Debugging.IsUserDebugging ?*/ Formatting.Indented /*: Formatting.None*/);

				if (string.IsNullOrEmpty(json)) {
					_logger.LogNullError(nameof(json));

					return;
				}

				string newFilePath = FilePath + ".new";

				// We always want to write entire content to temporary file first, in order to never load corrupted data, also when target file doesn't exist
				File.WriteAllText(newFilePath, json);

				if (File.Exists(FilePath)) {
					File.Replace(newFilePath, FilePath, null);
				} else {
					File.Move(newFilePath, FilePath);
				}
			} catch (Exception e) {
				_logger.LogGenericException(e);
			} finally {
				FileSemaphore.Release();
			}
		}
	}
}
