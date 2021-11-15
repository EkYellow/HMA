﻿using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models.Runs.Factory {
   /// <summary>
   /// Format Specifier:     []...
   /// Represents a general type of run where there are a number of sections, each with the same format, followed by an optional terminator.
   /// TableStreams are very broad in application. Basically, any table that points to another table, the inner table is a tablestream.
   /// TableStreams can nest.
   /// </summary>
   public class TableStreamRunContentStrategy : RunStrategy {
      public override int LengthForNewRun(IDataModel model, int pointerAddress) {
         var tableRun = GetTable(model, pointerAddress);
         var pointerSegment = GetSegment(tableRun, pointerAddress);
         TableStreamRun.TryParseTableStream(model, -1, new SortedSpan<int>(pointerAddress), pointerSegment.Name, pointerSegment.InnerFormat, tableRun.ElementContent, out var newStream);
         return newStream.Length;
      }
      public override bool TryAddFormatAtDestination(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments, int parentIndex) {
         if (TableStreamRun.TryParseTableStream(owner, destination, new SortedSpan<int>(source), name, Format, sourceSegments, out var tsRun)) {
            if (!(token is NoDataChangeDeltaModel)) owner.ObserveRunWritten(token, tsRun);
            return true;
         }
         return false;
      }
      public override bool Matches(IFormattedRun run) => run is TableStreamRun streamRun && streamRun.FormatString == Format;
      public override IFormattedRun WriteNewRun(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments) {
         // don't bother checking the TryParse result: we very much expect that the data originally in the run won't fit the parse.
         TableStreamRun.TryParseTableStream(owner, destination, new SortedSpan<int>(source), name, Format, sourceSegments, out var tableStream);
         if (TableStreamRun.TryWriteNewEndToken(token, ref tableStream)) return tableStream;
         return tableStream.DeserializeRun("", token, out var _);
      }
      public override void UpdateNewRunFromPointerFormat(IDataModel model, ModelDelta token, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments, int parentIndex, ref IFormattedRun run) {
         if (!TableStreamRun.TryParseTableStream(model, run.Start, run.PointerSources, name, Format, sourceSegments, out var runAttempt)) return;
         model.ClearFormat(token, run.Start, runAttempt.Length);
         run = runAttempt;
      }
      public override ErrorInfo TryParseData(IDataModel model, string name, int dataIndex, ref IFormattedRun run) {
         var pointerSources = model.GetUnmappedSourcesToAnchor(name);
         pointerSources = new SortedSpan<int>(pointerSources.Where(source => model.ReadValue(source) == 0).ToList());
         var errorInfo = ArrayRun.TryParse(model, name, Format, dataIndex, pointerSources, out var arrayRun);
         if (errorInfo == ErrorInfo.NoError) {
            run = arrayRun;
         } else if (Format != string.Empty) {
            return new ErrorInfo($"Format {Format} was not understood: " + errorInfo.ErrorMessage);
         }

         return ErrorInfo.NoError;
      }
   }
}
