using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2.Discovery;


namespace sc {
	public sealed class GlobalDatabase : SerializableFile {
		[JsonProperty(PropertyName = "_CellID", Required = Required.DisallowNull)]
		private uint BackingCellID;

		//internal readonly InMemoryServerListProvider ServerListProvider = new InMemoryServerListProvider();

		[JsonConstructor]
		private GlobalDatabase() {
		}

		private GlobalDatabase(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		internal uint CellID {
			get => BackingCellID;

			set {
				if (BackingCellID == value) {
					return;
				}

				BackingCellID = value;
				Utilities.InBackground(Save);
			}
		}

		internal static GlobalDatabase CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return new GlobalDatabase(filePath);
			}

			GlobalDatabase globalDatabase;

			try {
				string json = File.ReadAllText(filePath);
				if (string.IsNullOrEmpty(json)) {
					sc.Logger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(json)));
					return null;
				}

				globalDatabase = JsonConvert.DeserializeObject<GlobalDatabase>(json);
			} catch (Exception e) {
				sc.Logger.LogGenericWarningException(e);
				return null;
			}

			if (globalDatabase == null) {
				sc.Logger.LogNullError(nameof(globalDatabase));
				return null;
			}

			globalDatabase.FilePath = filePath;

			return globalDatabase;
		}
		/*	public sealed class ConcurrentHashSet<T> : IReadOnlyCollection<T>, ISet<T> {
		public int Count => BackingCollection.Count;
		public bool IsReadOnly => false;

		private readonly ConcurrentDictionary<T, bool> BackingCollection;

		public ConcurrentHashSet() => BackingCollection = new ConcurrentDictionary<T, bool>();

		public ConcurrentHashSet([NotNull] IEqualityComparer<T> comparer) {
			if (comparer == null) {
				throw new ArgumentNullException(nameof(comparer));
			}

			BackingCollection = new ConcurrentDictionary<T, bool>(comparer);
		}

		public bool Add(T item) => BackingCollection.TryAdd(item, true);
		public void Clear() => BackingCollection.Clear();

		[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
		public bool Contains(T item) => BackingCollection.ContainsKey(item);

		public void CopyTo(T[] array, int arrayIndex) => BackingCollection.Keys.CopyTo(array, arrayIndex);

		public void ExceptWith(IEnumerable<T> other) {
			foreach (T item in other) {
				Remove(item);
			}
		}

		public IEnumerator<T> GetEnumerator() => BackingCollection.Keys.GetEnumerator();

		public void IntersectWith(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			foreach (T item in this.Where(item => !otherSet.Contains(item))) {
				Remove(item);
			}
		}

		public bool IsProperSubsetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return (otherSet.Count > Count) && IsSubsetOf(otherSet);
		}

		public bool IsProperSupersetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return (otherSet.Count < Count) && IsSupersetOf(otherSet);
		}

		public bool IsSubsetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return this.All(otherSet.Contains);
		}

		public bool IsSupersetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return otherSet.All(Contains);
		}

		public bool Overlaps(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return otherSet.Any(Contains);
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
		public bool Remove(T item) => BackingCollection.TryRemove(item, out _);

		public bool SetEquals(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return (otherSet.Count == Count) && otherSet.All(Contains);
		}

		public void SymmetricExceptWith(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();
			HashSet<T> removed = new HashSet<T>();

			foreach (T item in otherSet.Where(Contains)) {
				removed.Add(item);
				Remove(item);
			}

			foreach (T item in otherSet.Where(item => !removed.Contains(item))) {
				Add(item);
			}
		}

		public void UnionWith(IEnumerable<T> other) {
			foreach (T otherElement in other) {
				Add(otherElement);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AnnotationConflictInHierarchy")]
		[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
		void ICollection<T>.Add([NotNull] T item) => Add(item);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		// We use Count() and not Any() because we must ensure full loop pass
		[PublicAPI]
		public bool AddRange([NotNull] IEnumerable<T> items) => items.Count(Add) > 0;

		// We use Count() and not Any() because we must ensure full loop pass
		[PublicAPI]
		public bool RemoveRange([NotNull] IEnumerable<T> items) => items.Count(Remove) > 0;

		[PublicAPI]
		public bool ReplaceIfNeededWith([NotNull] IReadOnlyCollection<T> other) {
			if (SetEquals(other)) {
				return false;
			}

			ReplaceWith(other);

			return true;
		}

		[PublicAPI]
		public void ReplaceWith([NotNull] IEnumerable<T> other) {
			BackingCollection.Clear();

			foreach (T item in other) {
				BackingCollection[item] = true;
			}
		}
	}

		internal sealed class InMemoryServerListProvider : IServerListProvider {
			[JsonProperty(Required = Required.DisallowNull)]
			private readonly ConcurrentHashSet<ServerRecordEndPoint> ServerRecords = new ConcurrentHashSet<ServerRecordEndPoint>();

			[NotNull]
			public Task<IEnumerable<ServerRecord>> FetchServerListAsync() => Task.FromResult(ServerRecords.Select(server => ServerRecord.CreateServer(server.Host, server.Port, server.ProtocolTypes)));

			[NotNull]
			public Task UpdateServerListAsync(IEnumerable<ServerRecord> endpoints) {
				if (endpoints == null) {
					sc.Logger.LogNullError(nameof(endpoints));

					return Task.CompletedTask;
				}

				HashSet<ServerRecordEndPoint> newServerRecords = endpoints.Select(ep => new ServerRecordEndPoint(ep.GetHost(), (ushort) ep.GetPort(), ep.ProtocolTypes)).ToHashSet();

				if (!ServerRecords.ReplaceIfNeededWith(newServerRecords)) {
					return Task.CompletedTask;
				}

				ServerListUpdated?.Invoke(this, EventArgs.Empty);

				return Task.CompletedTask;
			}

			public bool ShouldSerializeServerRecords() => ServerRecords.Count > 0;

			internal event EventHandler ServerListUpdated;
		}
*/
	}
}
