﻿using System;
using System.Runtime.CompilerServices;

namespace Corax.Queries;

unsafe partial struct SortingMatch
{
    public unsafe struct AlphanumericAscendingMatchComparer : IMatchComparer
    {
        private readonly IndexSearcher _searcher;
        private readonly int _fieldId;
        private readonly delegate*<ref AlphanumericAscendingMatchComparer, long, long, int> _compareFunc;
        private readonly MatchCompareFieldType _fieldType;

        public int FieldId => _fieldId;
        public MatchCompareFieldType FieldType => _fieldType;

        public AlphanumericAscendingMatchComparer(IndexSearcher searcher, int fieldId, MatchCompareFieldType entryFieldType)
        {
            _searcher = searcher;
            _fieldId = fieldId;
            _fieldType = entryFieldType;

            static int CompareWithLoadSequence(ref AlphanumericAscendingMatchComparer comparer, long x, long y)
            {
                var readerX = comparer._searcher.GetReaderFor(x);
                var readX = readerX.GetReaderFor(comparer._fieldId).Read(out var resultX);

                var readerY = comparer._searcher.GetReaderFor(y);
                var readY = readerY.GetReaderFor(comparer._fieldId).Read(out var resultY);

                if (readX && readY)
                {
                    return comparer.CompareSequence(resultX, resultY);
                }
                else if (readX)
                    return 1;

                return -1;
            }

            static int CompareWithLoadNumerical<T>(ref AlphanumericAscendingMatchComparer comparer, long x, long y) where T : unmanaged
            {
                var readerX = comparer._searcher.GetReaderFor(x);
                var readX = readerX.GetReaderFor(comparer._fieldId).Read<T>(out var resultX);

                var readerY = comparer._searcher.GetReaderFor(y);
                var readY = readerY.GetReaderFor(comparer._fieldId).Read<T>(out var resultY);

                if (readX && readY)
                {
                    return comparer.CompareNumerical(resultX, resultY);
                }
                else if (readX)
                    return 1;

                return -1;
            }

            _compareFunc = entryFieldType switch
            {
                MatchCompareFieldType.Integer => &CompareWithLoadNumerical<long>,
                MatchCompareFieldType.Floating => &CompareWithLoadNumerical<double>,
                _ => &CompareWithLoadSequence
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareById(long idx, long idy)
        {
            return _compareFunc(ref this, idx, idy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareNumerical<T>(T sx, T sy) where T : unmanaged
        {
            return BasicComparers.CompareAscending(sx, sy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareSequence(ReadOnlySpan<byte> sx, ReadOnlySpan<byte> sy)
        {
            return BasicComparers.CompareAlphanumericAscending(sx, sy);
        }
    }
}
