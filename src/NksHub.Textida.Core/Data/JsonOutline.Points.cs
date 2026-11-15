namespace NksHub.Textida.Core.Data;

public sealed partial class JsonOutline
{
    // All access is under _gate. A suffix edit touches blocks of 256 states, not every state of the file.
    private sealed class PointBlocks
    {
        private const int PointsPerBlock = 256;
        private readonly List<PointBlock> blocks = [];

        public int Count { get; private set; }
        public ScanState this[int index] => blocks[index / PointsPerBlock][index % PointsPerBlock];

        public void Add(ScanState point)
        {
            if (Count % PointsPerBlock == 0) blocks.Add(new PointBlock());
            blocks[^1].Add(point);
            Count++;
        }

        public void Truncate(int count)
        {
            int keep = (count + PointsPerBlock - 1) / PointsPerBlock;
            blocks.RemoveRange(keep, blocks.Count - keep);
            blocks[^1].Truncate((count - 1) % PointsPerBlock + 1);
            Count = count;
        }

        public void ShiftAfter(int segment, long start, long delta)
        {
            if (delta == 0) return;
            int first = segment + 1;
            for (int block = first / PointsPerBlock; block < blocks.Count; block++)
            {
                int from = block == first / PointsPerBlock ? first % PointsPerBlock : 0;
                if (from == 0) blocks[block].ShiftAll(start, delta);
                else blocks[block].ShiftTail(from, start, delta);
            }
        }

        private struct OffsetBounds()
        {
            public long Min = long.MaxValue;
            public long Max = long.MinValue;

            public void Include(long value)
            {
                if (value < 0) return;
                Min = Math.Min(Min, value);
                Max = Math.Max(Max, value);
            }

            public readonly bool Straddles(long start) => Min < start && Max >= start;

            public void Shift(long delta)
            {
                Min += delta;
                Max += delta;
            }
        }

        private sealed class PointBlock
        {
            private readonly ScanState[] points = new ScanState[PointsPerBlock];
            private long offsetShift, errorShift, keyStartShift, keyEndShift;
            private OffsetBounds error = new(), keyStart = new(), keyEnd = new();

            private int Count { get; set; }

            public ScanState this[int index]
            {
                get
                {
                    var point = points[index];
                    return point with
                    {
                        Offset = point.Offset + offsetShift,
                        ErrorAt = AddIfPresent(point.ErrorAt, errorShift),
                        KeyStart = AddIfPresent(point.KeyStart, keyStartShift),
                        KeyEnd = AddIfPresent(point.KeyEnd, keyEndShift),
                    };
                }
            }

            private static long AddIfPresent(long value, long delta) => value < 0 ? value : value + delta;

            public void Add(ScanState point)
            {
                Materialize();
                points[Count++] = point;
                Include(point);
            }

            public void Truncate(int count)
            {
                Materialize();
                Array.Clear(points, count, Count - count);
                Count = count;
                Recount();
            }

            public void ShiftAll(long start, long delta)
            {
                // JSONC may keep the key before a long comment. Its offsets need not move with the cursor.
                if (error.Straddles(start) || keyStart.Straddles(start) || keyEnd.Straddles(start))
                {
                    ShiftTail(0, start, delta);
                    return;
                }
                offsetShift += delta;
                ShiftField(ref errorShift, ref error, start, delta);
                ShiftField(ref keyStartShift, ref keyStart, start, delta);
                ShiftField(ref keyEndShift, ref keyEnd, start, delta);
            }

            private static void ShiftField(ref long shift, ref OffsetBounds bounds, long start, long delta)
            {
                if (bounds.Max < start) return;
                shift += delta;
                bounds.Shift(delta);
            }

            public void ShiftTail(int first, long start, long delta)
            {
                Materialize();
                for (int i = first; i < Count; i++)
                {
                    var point = points[i];
                    points[i] = point with
                    {
                        Offset = point.Offset + delta,
                        ErrorAt = Shift(point.ErrorAt, start, delta),
                        KeyStart = Shift(point.KeyStart, start, delta),
                        KeyEnd = Shift(point.KeyEnd, start, delta),
                    };
                }
                Recount();
            }

            private void Materialize()
            {
                if ((offsetShift | errorShift | keyStartShift | keyEndShift) == 0) return;
                for (int i = 0; i < Count; i++) points[i] = this[i];
                offsetShift = errorShift = keyStartShift = keyEndShift = 0;
            }

            private void Include(ScanState point)
            {
                error.Include(point.ErrorAt);
                keyStart.Include(point.KeyStart);
                keyEnd.Include(point.KeyEnd);
            }

            private void Recount()
            {
                error = keyStart = keyEnd = new OffsetBounds();
                for (int i = 0; i < Count; i++) Include(points[i]);
            }
        }
    }
}
