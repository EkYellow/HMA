﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.Models.Runs.Factory;
using HavenSoft.HexManiac.Core.Models.Runs.Sprites;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;

namespace HavenSoft.HexManiac.Core.ViewModels.Tools {
   public class TableTool : ViewModelCore, IToolViewModel {
      private readonly IDataModel model;
      private readonly Selection selection;
      private readonly ChangeHistory<ModelDelta> history;
      private readonly ViewPort viewPort;
      private readonly IToolTrayViewModel toolTray;

      public string Name => "Table";

      public IReadOnlyList<string> TableSections {
         get {
            var sections = UnmatchedArrays.Select(array => {
               var parts = model.GetAnchorFromAddress(-1, array.Start).Split('.');
               if (parts.Length > 2) return string.Join(".", parts.Take(2));
               return parts[0];
            }).Distinct().ToList();
            sections.Sort();
            return sections;
         }
      }

      private int selectedTableSection;
      public int SelectedTableSection {
         get => selectedTableSection;
         set => Set(ref selectedTableSection, value, UpdateTableList);
      }

      public IReadOnlyList<string> TableList {
         get {
            if (selectedTableSection == -1 || selectedTableSection >= TableSections.Count) return new string[0];
            var selectedSection = TableSections[selectedTableSection];
            var tableList = UnmatchedArrays
               .Select(array => model.GetAnchorFromAddress(-1, array.Start))
               .Where(name => name.StartsWith(selectedSection + "."))
               .Select(name => name.Substring(selectedSection.Length + 1))
               .ToList();
            tableList.Sort();
            return tableList;
         }
      }

      private int selectedTableIndex;
      public int SelectedTableIndex {
         get => selectedTableIndex;
         set {
            if (!TryUpdate(ref selectedTableIndex, value)) return;
            if (selectedTableIndex == -1 || dataForCurrentRunChangeUpdate) return;
            UpdateAddressFromSectionAndSelection();
         }
      }
      private void UpdateTableList(int oldValue = default) {
         NotifyPropertyChanged(nameof(TableList));
         UpdateAddressFromSectionAndSelection();
      }
      private void UpdateAddressFromSectionAndSelection(int oldValue = default) {
         if (selectedTableSection == -1 || selectedTableIndex == -1) return;
         var arrayName = TableSections[selectedTableSection];
         var tableList = TableList;
         if (selectedTableIndex >= tableList.Count) TryUpdate(ref selectedTableIndex, 0, nameof(SelectedTableIndex));
         arrayName += '.' + tableList[selectedTableIndex];
         var start = model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, arrayName);
         selection.GotoAddress(start);
         Address = start;
      }

      private IReadOnlyList<ArrayRun> UnmatchedArrays => model.Arrays.Where(a => string.IsNullOrEmpty(a.LengthFromAnchor)).ToList();

      private string currentElementName;
      public string CurrentElementName {
         get => currentElementName;
         private set => TryUpdate(ref currentElementName, value);
      }

      public IndexComboBoxViewModel CurrentElementSelector { get; }

      private readonly StubCommand previous, next, append;
      private StubCommand incrementAdd, decrementAdd;
      public ICommand Previous => previous;
      public ICommand Next => next;
      public ICommand Append => append;
      public ICommand IncrementAdd => StubCommand(ref incrementAdd, IncrementAddExecute, IncrementAddCanExecute);
      public ICommand DecrementAdd => StubCommand(ref decrementAdd, DecrementAddExecute, DecrementAddCanExecute);
      private void CommandCanExecuteChanged() {
         previous.RaiseCanExecuteChanged();
         next.RaiseCanExecuteChanged();
         append.RaiseCanExecuteChanged();
         incrementAdd.RaiseCanExecuteChanged();
         decrementAdd.RaiseCanExecuteChanged();
      }
      private void IncrementAddExecute() { AddCount += 1; CommandCanExecuteChanged(); }
      private void DecrementAddExecute() { AddCount -= 1; CommandCanExecuteChanged(); }
      private bool IncrementAddCanExecute() => append.CanExecute(null) && addCount < 500;
      private bool DecrementAddCanExecute() => append.CanExecute(null) && addCount > 1;

      private int addCount = 1;
      public int AddCount {
         get => addCount;
         set {
            value = Math.Min(Math.Max(1, value), 500);
            Set(ref addCount, value, arg => CommandCanExecuteChanged());
         }
      }

      public ObservableCollection<IArrayElementViewModel> Children { get; }

      // the address is the address not of the entire array, but of the current index of the array
      private int address = Pointer.NULL;
      public int Address {
         get => address;
         set {
            if (TryUpdate(ref address, value)) {
               var run = model.GetNextRun(value);
               if (run.Start > value || !(run is ITableRun)) {
                  Enabled = false;
                  CommandCanExecuteChanged();
                  return;
               }

               CommandCanExecuteChanged();
               Enabled = true;
               toolTray.Schedule(DataForCurrentRunChanged);
            }
         }
      }

      private bool enabled;
      public bool Enabled {
         get => enabled;
         private set => TryUpdate(ref enabled, value);
      }

      private string fieldFilter = string.Empty;
      public string FieldFilter {
         get => fieldFilter;
         set => Set(ref fieldFilter, value, oldVal => ApplyFieldFilter());
      }

#pragma warning disable 0067 // it's ok if events are never used after implementing an interface
      public event EventHandler<IFormattedRun> ModelDataChanged;
      public event EventHandler<string> OnError;
      public event EventHandler<string> OnMessage;
      public event EventHandler RequestMenuClose;
      public event EventHandler<(int originalLocation, int newLocation)> ModelDataMoved; // invoke when a new item gets added and the table has to move
#pragma warning restore 0067

      // properties that exist solely so the UI can remember things when the tab switches
      public double VerticalOffset { get; set; }

      public TableTool(IDataModel model, Selection selection, ChangeHistory<ModelDelta> history, ViewPort viewPort, IToolTrayViewModel toolTray) {
         this.model = model;
         this.selection = selection;
         this.history = history;
         this.viewPort = viewPort;
         this.toolTray = toolTray;
         CurrentElementSelector = new IndexComboBoxViewModel(viewPort.Model);
         CurrentElementSelector.UpdateSelection += UpdateViewPortSelectionFromTableComboBoxIndex;
         Children = new ObservableCollection<IArrayElementViewModel>();

         previous = new StubCommand {
            CanExecute = parameter => {
               var array = model.GetNextRun(address) as ITableRun;
               return array != null && array.Start < address;
            },
            Execute = parameter => {
               var array = (ITableRun)model.GetNextRun(address);
               selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(Address - array.ElementLength);
               selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(selection.Scroll.ViewPointToDataIndex(selection.SelectionStart) + array.ElementLength - 1);
            }
         };

         next = new StubCommand {
            CanExecute = parameter => {
               var array = model.GetNextRun(address) as ITableRun;
               return array != null && array.Start + array.Length > address + array.ElementLength;
            },
            Execute = parameter => {
               var address = this.address;
               var array = (ITableRun)model.GetNextRun(address);
               if (selection.Scroll.DataIndex < array.Start || selection.Scroll.DataIndex > array.Start + array.Length) selection.GotoAddress(array.Start);
               selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(address + array.ElementLength);
               selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(selection.Scroll.ViewPointToDataIndex(selection.SelectionStart) + array.ElementLength - 1);
            }
         };

         append = new StubCommand {
            CanExecute = parameter => {
               var array = model.GetNextRun(address) as ITableRun;
               return array != null && array.Start + array.Length == address + array.ElementLength;
            },
            Execute = parameter => {
               using (ModelCacheScope.CreateScope(model)) {
                  var array = (ITableRun)model.GetNextRun(address);
                  var originalArray = array;
                  var initialViewOffset = viewPort.DataOffset;
                  var error = model.CompleteArrayExtension(viewPort.CurrentChange, addCount, ref array);
                  if (array != null) {
                     if (array.Start != originalArray.Start) {
                        ModelDataMoved?.Invoke(this, (originalArray.Start, array.Start));
                        viewPort.Goto.Execute(array.Start + (initialViewOffset - originalArray.Start));
                        selection.SelectionStart = viewPort.ConvertAddressToViewPoint(array.Start + array.Length - array.ElementLength);
                     }
                     if (error.HasError && !error.IsWarning) {
                        OnError?.Invoke(this, error.ErrorMessage);
                     } else {
                        if (error.IsWarning) OnMessage?.Invoke(this, error.ErrorMessage);
                        ModelDataChanged?.Invoke(this, array);
                        selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(array.Start + array.Length - array.ElementLength);
                        selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(selection.Scroll.ViewPointToDataIndex(selection.SelectionStart) + array.ElementLength - 1);
                     }
                  }
                  RequestMenuClose?.Invoke(this, EventArgs.Empty);
                  if (model is PokemonModel pModel) pModel.ResolveConflicts();
               }
               AddCount = 1;
            }
         };

         CurrentElementName = "The Table tool only works if your cursor is on table data.";
      }

      private int childInsertionIndex = 0;
      private void AddChild(IArrayElementViewModel child) {
         if (childInsertionIndex == Children.Count) {
            Children.Add(child);
         } else if (!Children[childInsertionIndex].TryCopy(child)) {
            Children[childInsertionIndex] = child;
         }
         childInsertionIndex++;
      }

      private bool dataForCurrentRunChangeUpdate;
      public void DataForCurrentRunChanged() {
         foreach (var child in Children) child.DataChanged -= ForwardModelChanged;
         childInsertionIndex = 0;

         var array = model.GetNextRun(Address) as ITableRun;
         if (array == null || array.Start > Address) {
            CurrentElementName = "The Table tool only works if your cursor is on table data.";
            Children.Clear();
            NotifyPropertyChanged(nameof(TableSections));
            return;
         }

         dataForCurrentRunChangeUpdate = true;
         var basename = model.GetAnchorFromAddress(-1, array.Start);
         var anchorParts = basename.Split('.');
         NotifyPropertyChanged(nameof(TableSections));
         if (anchorParts.Length == 1) {
            TryUpdate(ref selectedTableSection, TableSections.IndexOf(anchorParts[0]));
            NotifyPropertyChanged(nameof(TableList));
         } else if (anchorParts.Length == 2) {
            TryUpdate(ref selectedTableSection, TableSections.IndexOf(anchorParts[0]));
            NotifyPropertyChanged(nameof(TableList));
            TryUpdate(ref selectedTableIndex, TableList.IndexOf(anchorParts[1]));
         } else {
            TryUpdate(ref selectedTableSection, TableSections.IndexOf(anchorParts[0] + "." + anchorParts[1]), nameof(SelectedTableSection));
            NotifyPropertyChanged(nameof(TableList));
            TryUpdate(ref selectedTableIndex, TableList.IndexOf(string.Join(".", anchorParts.Skip(2))), nameof(SelectedTableIndex));
         }

         dataForCurrentRunChangeUpdate = false;
         if (string.IsNullOrEmpty(basename)) basename = array.Start.ToString("X6");
         var index = (Address - array.Start) / array.ElementLength;

         if (0 <= index && index < array.ElementCount) {
            if (array.ElementNames.Count > index) {
               CurrentElementName = $"{basename}/{index}" + Environment.NewLine + $"{basename}/{array.ElementNames[index]}";
            } else {
               CurrentElementName = $"{basename}/{index}";
            }
            UpdateCurrentElementSelector(array, index);

            var elementOffset = array.Start + array.ElementLength * index;
            AddChild(new SplitterArrayElementViewModel(viewPort, basename, elementOffset));
            AddChildrenFromTable(array, index);

            if (array is ArrayRun arrayRun) {
               index -= arrayRun.ParentOffset.BeginningMargin;
               if (!string.IsNullOrEmpty(arrayRun.LengthFromAnchor) && model.GetMatchedWords(arrayRun.LengthFromAnchor).Count == 0) basename = arrayRun.LengthFromAnchor; // basename is now a 'parent table' name, if there is one

               foreach (var currentArray in model.GetRelatedArrays(arrayRun)) {
                  if (currentArray == arrayRun) continue;
                  var currentArrayName = model.GetAnchorFromAddress(-1, currentArray.Start);
                  var currentIndex = index + currentArray.ParentOffset.BeginningMargin;
                  if (currentIndex >= 0 && currentIndex < currentArray.ElementCount) {
                     elementOffset = currentArray.Start + currentArray.ElementLength * currentIndex;
                     AddChild(new SplitterArrayElementViewModel(viewPort, currentArrayName, elementOffset));
                     AddChildrenFromTable(currentArray, currentIndex);
                  }
               }
            }

            AddChildrenFromStreams(array, basename, index);
         }

         while (Children.Count > childInsertionIndex) Children.RemoveAt(Children.Count - 1);
         foreach (var child in Children) child.DataChanged += ForwardModelChanged;

         var paletteIndex = Children.Where(child => child is SpriteElementViewModel).Select(c => {
            var spriteElement = (SpriteElementViewModel)c;
            if (spriteElement.CurrentPalette > spriteElement.MaxPalette) return 0;
            return spriteElement.CurrentPalette;
         }).Concat(1.Range()).Max();
         foreach (var child in Children) {
            // update sprites now that all the associated palettes have been loaded.
            if (child is SpriteElementViewModel sevm) {
               sevm.CurrentPalette = paletteIndex;
               sevm.UpdateTiles();
            }
            // update 'visible' for children based on their parents.
            if (child is SplitterArrayElementViewModel splitter) splitter.UpdateCollapsed(fieldFilter);
         }
      }

      private void UpdateCurrentElementSelector(ITableRun array, int index) {
         CurrentElementSelector.SetupFromModel(array.Start + array.ElementLength * index);
      }

      private void UpdateViewPortSelectionFromTableComboBoxIndex(object sender = null, EventArgs e = null) {
         var array = (ITableRun)model.GetNextRun(Address);
         var address = array.Start + array.ElementLength * CurrentElementSelector.SelectedIndex;
         selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(address);
         selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(address + array.ElementLength - 1);
      }

      private void AddChildrenFromStreams(ITableRun array, string basename, int index) {
         var plmResults = new List<(int, int)>();
         var eggResults = new List<(int, int)>();
         var trainerResults = new List<int>();
         var streamResults = new List<(int, int)>();
         foreach (var child in model.Streams) {
            if (!child.DependsOn(basename)) continue;
            if (child is PLMRun plmRun) plmResults.AddRange(plmRun.Search(index));
            if (child is EggMoveRun eggRun) eggResults.AddRange(eggRun.Search(basename, index));
            if (child is TrainerPokemonTeamRun trainerRun) trainerResults.AddRange(trainerRun.Search(basename, index));
            if (child is TableStreamRun streamRun) streamResults.AddRange(streamRun.Search(basename, index));
         }
         var parentOffset = array is ArrayRun arrayRun ? arrayRun.ParentOffset.BeginningMargin : 0;
         var elementName = array.ElementNames.Count > index + parentOffset && index + parentOffset >= 0 ? array.ElementNames[index + parentOffset] : "Element " + index;
         if (eggResults.Count > 0) {
            AddChild(new ButtonArrayElementViewModel("Show uses in egg moves.", () => {
               using (ModelCacheScope.CreateScope(model)) {
                  viewPort.OpenSearchResultsTab($"{elementName} within {HardcodeTablesModel.EggMovesTableName}", eggResults);
               }
            }));
         }
         if (plmResults.Count > 0) {
            AddChild(new ButtonArrayElementViewModel("Show uses in level-up moves.", () => {
               using (ModelCacheScope.CreateScope(model)) {
                  viewPort.OpenSearchResultsTab($"{elementName} within {HardcodeTablesModel.LevelMovesTableName}", plmResults);
               }
            }));
         }
         if (trainerResults.Count > 0) {
            var selections = trainerResults.Select(result => (result, result + 1)).ToList();
            AddChild(new ButtonArrayElementViewModel("Show uses in trainer teams.", () => {
               using (ModelCacheScope.CreateScope(model)) {
                  viewPort.OpenSearchResultsTab($"{elementName} within {HardcodeTablesModel.TrainerTableName}", selections);
               }
            }));
         }
         if (streamResults.Count > 0) {
            AddChild(new ButtonArrayElementViewModel("Show uses in other streams.", () => {
               viewPort.OpenSearchResultsTab($"{elementName} within streams", streamResults);
            }));
         }

         foreach (var table in model.Arrays) {
            if (!table.DependsOn(basename)) continue;
            var results = new List<(int, int)>(table.Search(model, basename, index));
            if (results.Count == 0) continue;
            var name = model.GetAnchorFromAddress(-1, table.Start);
            AddChild(new ButtonArrayElementViewModel($"Show uses in {name}.", () => {
               viewPort.OpenSearchResultsTab($"{elementName} within {name}", results);
            }));
         }
      }

      private void AddChildrenFromTable(ITableRun table, int index) {
         var itemAddress = table.Start + table.ElementLength * index;
         foreach (var itemSegment in table.ElementContent) {
            var item = itemSegment;
            if (item is ArrayRunRecordSegment recordItem) item = recordItem.CreateConcrete(model, itemAddress);
            IArrayElementViewModel viewModel = null;
            if (item.Type == ElementContentType.Unknown) viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, HexFieldStratgy.Instance);
            else if (item.Type == ElementContentType.PCS) viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, new TextFieldStrategy());
            else if (item.Type == ElementContentType.Pointer) viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, new AddressFieldStratgy());
            else if (item.Type == ElementContentType.BitArray) viewModel = new BitListArrayElementViewModel(selection, history, model, item.Name, itemAddress);
            else if (item.Type == ElementContentType.Integer) {
               if (item is ArrayRunEnumSegment enumSegment) {
                  viewModel = new ComboBoxArrayElementViewModel(viewPort, selection, item.Name, itemAddress, item.Length);
                  var anchor = model.GetAnchorFromAddress(-1, table.Start);
                  var enumSourceTableStart = model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, enumSegment.EnumName);
                  if (!string.IsNullOrEmpty(anchor) && model.GetDependantArrays(anchor).Count() == 1 && enumSourceTableStart >= 0) {
                     AddChild(viewModel);
                     viewModel = new BitListArrayElementViewModel(selection, history, model, item.Name, itemAddress);
                  }
               } else if (item is ArrayRunTupleSegment tupleItem) {
                  viewModel = new TupleArrayElementViewModel(viewPort, tupleItem, itemAddress);
               } else if (item is ArrayRunHexSegment) {
                  viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, HexFieldStratgy.Instance);
               } else if (item is ArrayRunColorSegment) {
                  viewModel = new ColorFieldArrayElementViewModel(viewPort, item.Name, itemAddress);
               } else if (item is ArrayRunCalculatedSegment calcSeg) {
                  viewModel = new CalculatedElementViewModel(viewPort, calcSeg, itemAddress);
               } else {
                  viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, new NumericFieldStrategy());
               }
            } else {
               throw new NotImplementedException();
            }
            AddChild(viewModel);
            AddChildrenFromPointerSegment(itemAddress, item, childInsertionIndex - 1, recursionLevel: 0);
            itemAddress += item.Length;
         }
      }

      private void AddChildrenFromPointerSegment(int itemAddress, ArrayRunElementSegment item, int parentIndex, int recursionLevel) {
         if (!(item is ArrayRunPointerSegment pointerSegment)) return;
         if (pointerSegment.InnerFormat == string.Empty) return;
         var destination = model.ReadPointer(itemAddress);
         IFormattedRun streamRun = null;
         if (destination != Pointer.NULL) {
            streamRun = model.GetNextRun(destination);
            if (!pointerSegment.DestinationDataMatchesPointerFormat(model, new NoDataChangeDeltaModel(), itemAddress, destination, null, parentIndex)) streamRun = null;
            if (streamRun != null && streamRun.Start != destination) {
               // For some reason (possibly because of a run length conflict),
               //    the destination data appears to match the expected type,
               //    but there is no run for it.
               // Go ahead and generate a new temporary run for the data.
               var strategy = model.FormatRunFactory.GetStrategy(pointerSegment.InnerFormat);
               strategy.TryParseData(model, string.Empty, destination, ref streamRun);
            }
         }

         IStreamArrayElementViewModel streamElement = null;
         if (streamRun == null || streamRun is IStreamRun) streamElement = new TextStreamElementViewModel(viewPort, itemAddress, pointerSegment.InnerFormat);
         if (streamRun is ISpriteRun spriteRun) streamElement = new SpriteElementViewModel(viewPort, spriteRun.FormatString, spriteRun.SpriteFormat, itemAddress);
         if (streamRun is IPaletteRun paletteRun) streamElement = new PaletteElementViewModel(viewPort, history, paletteRun.FormatString, paletteRun.PaletteFormat, itemAddress);
         if (streamRun is TrainerPokemonTeamRun tptRun) streamElement = new TrainerPokemonTeamElementViewModel(viewPort, tptRun, itemAddress);
         if (streamElement == null) return;

         var streamAddress = itemAddress;
         var myIndex = childInsertionIndex;
         Children[parentIndex].DataChanged += (sender, e) => {
            var closure_destination = model.ReadPointer(streamAddress);
            var run = model.GetNextRun(closure_destination) as IStreamRun;
            IStreamArrayElementViewModel newStream = null;

            if (run == null || run is IStreamRun) newStream = new TextStreamElementViewModel(viewPort, streamAddress, pointerSegment.InnerFormat);
            if (run is ISpriteRun spriteRun1) newStream = new SpriteElementViewModel(viewPort, spriteRun1.FormatString, spriteRun1.SpriteFormat, streamAddress);
            if (run is IPaletteRun paletteRun1) newStream = new PaletteElementViewModel(viewPort, history, paletteRun1.FormatString, paletteRun1.PaletteFormat, streamAddress);

            newStream.DataChanged += ForwardModelChanged;
            newStream.DataMoved += ForwardModelDataMoved;
            if (!Children[myIndex].TryCopy(newStream)) Children[myIndex] = newStream;
         };
         streamElement.DataMoved += ForwardModelDataMoved;
         AddChild(streamElement);

         parentIndex = childInsertionIndex - 1;
         if (streamRun is ITableRun tableRun && recursionLevel < 1) {
            int segmentOffset = 0;
            for (int i = 0; i < tableRun.ElementContent.Count; i++) {
               if (!(tableRun.ElementContent[i] is ArrayRunPointerSegment)) { segmentOffset += tableRun.ElementContent[i].Length; continue; }
               for (int j = 0; j < tableRun.ElementCount; j++) {
                  itemAddress = tableRun.Start + segmentOffset + j * tableRun.ElementLength;
                  AddChildrenFromPointerSegment(itemAddress, tableRun.ElementContent[i], parentIndex, recursionLevel + 1);
               }
               segmentOffset += tableRun.ElementContent[i].Length;
            }
         }
      }

      private void ApplyFieldFilter() {
         foreach (var child in Children) {
            // update 'visible' for children based on their parents.
            if (child is SplitterArrayElementViewModel splitter) splitter.UpdateCollapsed(fieldFilter);
         }
      }

      private void ForwardModelChanged(object sender, EventArgs e) => ModelDataChanged?.Invoke(this, model.GetNextRun(Address));
      private void ForwardModelDataMoved(object sender, (int originalStart, int newStart) e) => ModelDataMoved?.Invoke(this, e);
   }
}
