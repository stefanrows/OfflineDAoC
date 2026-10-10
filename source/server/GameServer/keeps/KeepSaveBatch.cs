using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DOL.Database;

namespace DOL.GS.Keeps
{
    /// <summary>
    /// Unit of work for the rows of one keep operation: the keep, its components
    /// and its doors. A lord death, release or level change used to save each of
    /// them in its own SQLite transaction on the game tick, and re-read every
    /// component before each write (bug 134).
    /// Inside a scope, <see cref="Save"/> records a persisted row instead of
    /// writing it, and <see cref="Pending{TRow}"/> returns the recorded row so a
    /// component is read once. The outermost scope writes each dirty recorded row
    /// once, in one database transaction, when it ends; a nested scope joins the
    /// outer one. Outside a scope <see cref="Save"/> writes immediately, as before.
    /// The scope is thread-static: a save raised on another thread is never
    /// deferred into the game thread's batch.
    /// </summary>
    public sealed class KeepSaveBatch : IDisposable
    {
        private static readonly DOL.Logging.Logger log = DOL.Logging.LoggerManager.Create(typeof(KeepSaveBatch));

        [ThreadStatic] private static KeepSaveBatch _current;

        private readonly string _owner;
        private readonly AbstractGameKeep _keep;
        private readonly bool _joined;
        private readonly Dictionary<(Type Type, string ObjectId), DataObject> _rows = new();
        private bool _ended;

        private KeepSaveBatch(string owner, AbstractGameKeep keep, bool joined)
        {
            _owner = owner;
            _keep = keep;
            _joined = joined;
        }

        /// <summary>Opens a scope on this thread. The outermost scope flushes when disposed.</summary>
        public static KeepSaveBatch Begin(string owner, AbstractGameKeep keep)
        {
            if (_current != null)
                return new KeepSaveBatch(owner, keep, joined: true);

            _current = new KeepSaveBatch(owner, keep, joined: false);
            return _current;
        }

        /// <summary>Records a persisted row for the outermost flush, or writes it now when no scope is open.</summary>
        public static bool Save(DataObject row)
        {
            KeepSaveBatch batch = _current;
            if (batch == null || !row.IsPersisted)
                return GameServer.Database.SaveObject(row);

            batch._rows[(row.GetType(), row.ObjectId)] = row;
            return true;
        }

        /// <summary>The row this scope already holds for <paramref name="objectId"/>, or null.</summary>
        public static TRow Pending<TRow>(string objectId) where TRow : DataObject
        {
            KeepSaveBatch batch = _current;
            if (batch == null || objectId == null)
                return null;

            return batch._rows.TryGetValue((typeof(TRow), objectId), out DataObject row) ? (TRow)row : null;
        }

        /// <summary>Reads the rows of <typeparamref name="TRow"/> this scope will save in one
        /// query, so each is not read on its own connection. Rows already held are kept.</summary>
        public static void Prefetch<TRow>(IEnumerable<string> objectIds) where TRow : DataObject
        {
            KeepSaveBatch batch = _current;
            if (batch == null || objectIds == null)
                return;

            string[] ids = objectIds
                .Where(id => id != null && !batch._rows.ContainsKey((typeof(TRow), id)))
                .Distinct()
                .ToArray();
            if (ids.Length == 0)
                return;

            foreach (TRow row in GameServer.Database.FindObjectsByKey<TRow>(ids).OfType<TRow>())
                batch._rows[(typeof(TRow), row.ObjectId)] = row;
        }

        public void Dispose()
        {
            if (_joined || _ended)
                return;

            _ended = true;
            _current = null;
            Flush();
        }

        private void Flush()
        {
            DataObject[] dirty = _rows.Values.Where(row => row.Dirty).ToArray();
            _rows.Clear();
            if (dirty.Length == 0)
                return;

            long mark = Stopwatch.GetTimestamp();
            if (!WriteAtomically(dirty))
            {
                // Nothing was committed, or the database cannot commit several tables at
                // once. Write the rows table by table, as before, so one rejected row
                // cannot drop the keep or the other rows.
                if (GameServer.Database is SqlObjectDatabase)
                    log.Warn($"KEEP_SAVE_FALLBACK keep={_keep?.KeepID} owner={_owner} rows={dirty.Length}");
                GameServer.Database.SaveObject(dirty);
            }

            PvpKeepCampaign.LogSlowKeepStep(_keep, _owner, "flush", mark);
        }

        private static bool WriteAtomically(DataObject[] rows) =>
            GameServer.Database is SqlObjectDatabase sql && sql.SaveObjectsAtomically(rows);
    }
}
