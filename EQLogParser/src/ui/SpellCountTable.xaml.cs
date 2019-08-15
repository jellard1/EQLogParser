﻿using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EQLogParser
{
  /// <summary>
  /// Interaction logic for SpellCountGrid.xaml
  /// </summary>
  public partial class SpellCountTable : UserControl
  {
    private static readonly log4net.ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    private static string ICON_COLOR = "#5191c1";
    private static bool Running = false;

    private List<string> PlayerList;
    private SpellCountData TheSpellCounts;
    private ObservableCollection<SpellCountRow> SpellRowsView = new ObservableCollection<SpellCountRow>();
    private DictionaryAddHelper<string, uint> AddHelper = new DictionaryAddHelper<string, uint>();
    private Dictionary<string, byte> HiddenSpells = new Dictionary<string, byte>();
    private List<string> CastTypes = new List<string>() { "Cast And Received", "Cast Spells", "Received Spells" };
    private List<string> CountTypes = new List<string>() { "Show By Count", "Show By Percent" };
    private List<string> MinFreqs = new List<string>() { "Any Freq", "Freq > 1", "Freq > 2", "Freq > 3", "Freq > 4" };
    private List<string> SpellTypes = new List<string>() { "Any Type", "Beneficial", "Detrimental" };
    private int CurrentCastType = 0;
    private int CurrentCountType = 0;
    private int CurrentMinFreqCount = 0;
    private int CurrentSpellType = 0;
    private bool CurrentShowSelfOnly = false;
    private bool Ready = false;

    public SpellCountTable(string title)
    {
      InitializeComponent();
      titleLabel.Content = title;

      dataGrid.Sorting += (s, e2) =>
      {
        if (!string.IsNullOrEmpty(e2.Column.Header as string))
        {
          e2.Column.SortDirection = e2.Column.SortDirection ?? ListSortDirection.Ascending;
        }
      };

      dataGrid.ItemsSource = SpellRowsView;
      castTypes.ItemsSource = CastTypes;
      castTypes.SelectedIndex = 0;
      countTypes.ItemsSource = CountTypes;
      countTypes.SelectedIndex = 0;
      minFreqList.ItemsSource = MinFreqs;
      minFreqList.SelectedIndex = 0;
      spellTypes.ItemsSource = SpellTypes;
      spellTypes.SelectedIndex = 0;
      Ready = true;
    }

    public void ShowSpells(List<PlayerStats> selectedStats, CombinedStats currentStats)
    {
      var childStats = currentStats?.Children;
      var raidStats = currentStats?.RaidStats;

      if (selectedStats != null && raidStats != null)
      {
        PlayerList = new List<string>();
        foreach (var stats in selectedStats)
        {
          string name = stats.Name;
          if (childStats != null && childStats.ContainsKey(stats.Name) && childStats[stats.Name].Count > 1)
          {
            name = childStats[stats.Name].First().Name;
          }

          PlayerList.Add(name);
        }

        TheSpellCounts = SpellCountBuilder.GetSpellCounts(PlayerList, raidStats);

        if (TheSpellCounts.PlayerCastCounts.Count > 0)
        {
          selectAll.IsEnabled = true;
        }

        Display();
      }
    }

    private void Display()
    {
      if (Running == false)
      {
        Running = true;
        Dispatcher.InvokeAsync(() =>
        {
          castTypes.IsEnabled = countTypes.IsEnabled = minFreqList.IsEnabled = false;
          (Application.Current.MainWindow as MainWindow).Busy(true);
        });

        Task.Delay(20).ContinueWith(task =>
        {
          try
          {
            if (TheSpellCounts != null)
            {
              Dispatcher.InvokeAsync(() =>
              {
                dataGrid.Columns.Add(new DataGridTextColumn()
                {
                  Header = "",
                  Binding = new Binding("Spell"),
                  CellStyle = Application.Current.Resources["SpellGridNameCellStyle"] as Style
                });
              });

              Dictionary<string, Dictionary<string, uint>> filteredPlayerMap = new Dictionary<string, Dictionary<string, uint>>();
              Dictionary<string, uint> totalCountMap = new Dictionary<string, uint>();
              Dictionary<string, uint> uniqueSpellsMap = new Dictionary<string, uint>();

              uint totalCasts = 0;
              PlayerList.ForEach(player =>
              {
                filteredPlayerMap[player] = new Dictionary<string, uint>();

                if ((CurrentCastType == 0 || CurrentCastType == 1) && TheSpellCounts.PlayerCastCounts.ContainsKey(player))
                {
                  foreach (string id in TheSpellCounts.PlayerCastCounts[player].Keys)
                  {
                    totalCasts = UpdateMaps(id, player, TheSpellCounts.PlayerCastCounts[player][id], TheSpellCounts.MaxCastCounts, totalCountMap, uniqueSpellsMap, filteredPlayerMap, false, totalCasts);
                  }
                }

                if ((CurrentCastType == 0 || CurrentCastType == 2) && TheSpellCounts.PlayerReceivedCounts.ContainsKey(player))
                {
                  foreach (string id in TheSpellCounts.PlayerReceivedCounts[player].Keys)
                  {
                    totalCasts = UpdateMaps(id, player, TheSpellCounts.PlayerReceivedCounts[player][id], TheSpellCounts.MaxReceivedCounts, totalCountMap, uniqueSpellsMap, filteredPlayerMap, true, totalCasts);
                  }
                }
              });

              List<string> sortedPlayers = totalCountMap.Keys.OrderByDescending(key => totalCountMap[key]).ToList();
              List<string> sortedSpellList = uniqueSpellsMap.Keys.OrderByDescending(key => uniqueSpellsMap[key]).ToList();

              int colCount = 0;
              foreach (string name in sortedPlayers)
              {
                string colBinding = "Values[" + colCount + "]"; // dont use colCount directory since it will change during Dispatch
                double total = totalCountMap.ContainsKey(name) ? totalCountMap[name] : 0;
                string header = name + " = " + ((CurrentCountType == 0) ? total.ToString(CultureInfo.CurrentCulture) : Math.Round(total / totalCasts * 100, 2).ToString(CultureInfo.CurrentCulture));

                Dispatcher.InvokeAsync(() =>
                {
                  DataGridTextColumn col = new DataGridTextColumn() { Header = header, Binding = new Binding(colBinding) };
                  col.CellStyle = Application.Current.Resources["SpellGridDataCellStyle"] as Style;
                  col.HeaderStyle = Application.Current.Resources["BrightCenterGridHeaderStyle"] as Style;
                  dataGrid.Columns.Add(col);
                });

                Thread.Sleep(5);
                colCount++;
              }

              string totalHeader = CurrentCountType == 0 ? "Totals = " + totalCasts : "Totals = 100";
              Dispatcher.InvokeAsync(() =>
              {
                DataGridTextColumn col = new DataGridTextColumn() { Header = totalHeader, Binding = new Binding("Values[" + colCount + "]") };
                col.CellStyle = Application.Current.Resources["SpellGridDataCellStyle"] as Style;
                col.HeaderStyle = Application.Current.Resources["BrightCenterGridHeaderStyle"] as Style;
                dataGrid.Columns.Add(col);
              });

              int existingIndex = 0;
              foreach (var spell in sortedSpellList)
              {
                var row = (SpellRowsView.Count > existingIndex) ? SpellRowsView[existingIndex] : new SpellCountRow();

                row.Spell = spell;
                row.Values = new double[sortedPlayers.Count + 1];
                row.IsReceived = spell.StartsWith("Received", StringComparison.Ordinal);
                row.IconColor = ICON_COLOR;

                int i;
                for (i = 0; i < sortedPlayers.Count; i++)
                {
                  if (filteredPlayerMap.ContainsKey(sortedPlayers[i]))
                  {
                    if (filteredPlayerMap[sortedPlayers[i]].ContainsKey(spell))
                    {
                      if (CurrentCountType == 0)
                      {
                        row.Values[i] = filteredPlayerMap[sortedPlayers[i]][spell];
                      }
                      else
                      {
                        row.Values[i] = Math.Round((double)filteredPlayerMap[sortedPlayers[i]][spell] / totalCountMap[sortedPlayers[i]] * 100, 2);
                      }
                    }
                    else
                    {
                      row.Values[i] = CurrentCountType == 0 ? 0 : 0.0;
                    }
                  }
                }

                row.Values[i] = CurrentCountType == 0 ? uniqueSpellsMap[spell] : Math.Round((double)uniqueSpellsMap[spell] / totalCasts * 100, 2);

                if ((SpellRowsView.Count <= existingIndex))
                {
                  Dispatcher.InvokeAsync(() => SpellRowsView.Add(row));
                }

                existingIndex++;
                Thread.Sleep(5);
              }
            }
          }
          catch (Exception ex)
          {
            LOG.Error(ex);
            throw;
          }
          finally
          {
            Dispatcher.InvokeAsync(() =>
            {
              castTypes.IsEnabled = countTypes.IsEnabled = minFreqList.IsEnabled = true;
              (Application.Current.MainWindow as MainWindow).Busy(false);
              exportClick.IsEnabled = copyOptions.IsEnabled = removeRowClick.IsEnabled = SpellRowsView.Count > 0;
            });

            Running = false;
          }
        }, TaskScheduler.Default);
      }
    }

    private uint UpdateMaps(string id, string player, uint playerCount, Dictionary<string, uint> maxCounts, Dictionary<string, uint> totalCountMap,
      Dictionary<string, uint> uniqueSpellsMap, Dictionary<string, Dictionary<string, uint>> filteredPlayerMap, bool received, uint totalCasts)
    {
      var spellData = TheSpellCounts.UniqueSpells[id];
      if ((CurrentSpellType == 0 || (CurrentSpellType == 1 && spellData.Beneficial) || (CurrentSpellType == 2 && !spellData.Beneficial)) 
        && (!received || CurrentShowSelfOnly == true || spellData.LandsOnOther.Length > 0))
      {
        string name = spellData.SpellAbbrv;

        if (received)
        {
          name = "Received " + name;
        }

        if (!HiddenSpells.ContainsKey(name) && maxCounts[id] > CurrentMinFreqCount)
        {
          AddHelper.Add(totalCountMap, player, playerCount);
          AddHelper.Add(uniqueSpellsMap, name, playerCount);
          AddHelper.Add(filteredPlayerMap[player], name, playerCount);
          totalCasts += playerCount;
        }
      }

      return totalCasts;
    }

    private void OptionsChanged()
    {
      if (Ready)
      {
        for (int i = dataGrid.Columns.Count - 1; i > 0; i--)
        {
          dataGrid.Columns.RemoveAt(i);
        }

        CurrentCastType = castTypes.SelectedIndex;
        CurrentCountType = countTypes.SelectedIndex;
        CurrentMinFreqCount = minFreqList.SelectedIndex;
        CurrentSpellType = spellTypes.SelectedIndex;
        CurrentShowSelfOnly = showSelfOnly.IsChecked.Value;
        Display();
      }
    }

    private void Options_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (SpellRowsView.Count > 0)
      {
        SpellRowsView.Clear();
      }

      OptionsChanged();
    }

    private void SelfOnlyChange(object sender, RoutedEventArgs e)
    {
      if (SpellRowsView.Count > 0)
      {
        SpellRowsView.Clear();
      }

      OptionsChanged();
    }

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
      Helpers.DataGridSelectAll(sender);
    }

    private void UnselectAllClick(object sender, RoutedEventArgs e)
    {
      Helpers.DataGridUnselectAll(sender);
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      // adds a delay where a drag-select doesn't keep sending events

      DataGrid callingDataGrid = sender as DataGrid;
      selectAll.IsEnabled = (callingDataGrid.SelectedItems.Count < callingDataGrid.Items.Count) && callingDataGrid.Items.Count > 0;
      unselectAll.IsEnabled = callingDataGrid.SelectedItems.Count > 0 && callingDataGrid.Items.Count > 0;
    }

    private void CreateImageClick(object sender, RoutedEventArgs e)
    {
      // lame workaround to toggle scrollbar to fix UI
      dataGrid.IsEnabled = false;
      dataGrid.SelectedItems.Clear();
      dataGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
      dataGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;

      Task.Delay(50).ContinueWith((bleh) =>
      {
        Dispatcher.InvokeAsync(() =>
        {
          dataGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
          dataGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
          SpellRowsView.ToList().ForEach(spr => spr.IconColor = "#252526");
          dataGrid.Items.Refresh();

          Task.Delay(50).ContinueWith((bleh2) => Dispatcher.InvokeAsync(() => CreateImage()), TaskScheduler.Default);
        });
      }, TaskScheduler.Default);
    }

    private void CreateImage()
    {
      try
      {
        const int labelHeight = 16;
        const int margin = 4;

        var rowHeight = SpellRowsView.Count > 0 ? (dataGrid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow).ActualHeight : 0;
        var totalRowHeight = rowHeight * SpellRowsView.Count + rowHeight + 2; // add extra for header row and a little for the bottom border
        var totalColumnWidth = dataGrid.Columns.ToList().Sum(column => column.ActualWidth);
        var realTableHeight = dataGrid.ActualHeight < totalRowHeight ? dataGrid.ActualHeight : totalRowHeight;
        var realColumnWidth = dataGrid.ActualWidth < totalColumnWidth ? dataGrid.ActualWidth : totalColumnWidth;

        var dpiScale = VisualTreeHelper.GetDpi(dataGrid);
        RenderTargetBitmap rtb = new RenderTargetBitmap((int)realColumnWidth, (int) (realTableHeight + labelHeight + margin), dpiScale.PixelsPerInchX, dpiScale.PixelsPerInchY, PixelFormats.Pbgra32);

        DrawingVisual dv = new DrawingVisual();
        using (DrawingContext ctx = dv.RenderOpen())
        {
          var brush = new VisualBrush(titleLabel);
          ctx.DrawRectangle(brush, null, new Rect(new Point(4, margin / 2), new Size(titleLabel.ActualWidth, labelHeight)));
  
          brush = new VisualBrush(dataGrid);
          ctx.DrawRectangle(brush, null, new Rect(new Point(0, labelHeight + margin), new Size(dataGrid.ActualWidth, dataGrid.ActualHeight + SystemParameters.HorizontalScrollBarHeight)));
        }

        rtb.Render(dv);
        Clipboard.SetImage(rtb);

        SpellRowsView.ToList().ForEach(spr => spr.IconColor = ICON_COLOR);
        dataGrid.Items.Refresh();
      }
      catch(ExternalException ex)
      {
        LOG.Error("Could not Copy Image", ex);
      }
      catch (ThreadStateException ex)
      {
        LOG.Error("Could not Copy Image", ex);
      }
      catch (ArgumentNullException ex)
      {
        LOG.Error("Could not Copy Image", ex);
      }
      catch (NullReferenceException ex)
      {
        LOG.Error("Could not Copy Image", ex);
      }
      finally
      {
        dataGrid.IsEnabled = true;
      }
    }

    private void ReloadClick(object sender, RoutedEventArgs e)
    {
      HiddenSpells.Clear();
      SpellRowsView.Clear();
      OptionsChanged();
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
      try
      {
        // WPF doesn't have its own file chooser so use Win32 Version
        OpenFileDialog dialog = new OpenFileDialog
        {
          // filter to txt files
          DefaultExt = ".scf.gz",
          Filter = "Spell Count File (*.scf.gz) | *.scf.gz"
        };

        // show dialog and read result
        if (dialog.ShowDialog().Value)
        {
          FileInfo gzipFileName = new FileInfo(dialog.FileName);

          using (GZipStream decompressionStream = new GZipStream(gzipFileName.OpenRead(), CompressionMode.Decompress))
          {
            using (var reader = new StreamReader(decompressionStream))
            {
              string json = reader.ReadToEnd();
              reader.Close();

              var data = JsonConvert.DeserializeObject<SpellCountsSerialized>(json);

              // copy data
              PlayerList = PlayerList.Union(data.PlayerNames).ToList();

              foreach (var player in data.TheSpellData.PlayerCastCounts.Keys)
              {
                TheSpellCounts.PlayerCastCounts[player] = data.TheSpellData.PlayerCastCounts[player];
              }

              foreach (var player in data.TheSpellData.PlayerReceivedCounts.Keys)
              {
                TheSpellCounts.PlayerReceivedCounts[player] = data.TheSpellData.PlayerReceivedCounts[player];
              }

              foreach (var spellId in data.TheSpellData.MaxCastCounts.Keys)
              {
                if (!TheSpellCounts.MaxCastCounts.ContainsKey(spellId) || TheSpellCounts.MaxCastCounts[spellId] < data.TheSpellData.MaxCastCounts[spellId])
                {
                  TheSpellCounts.MaxCastCounts[spellId] = data.TheSpellData.MaxCastCounts[spellId];
                }
              }

              foreach (var spellId in data.TheSpellData.MaxReceivedCounts.Keys)
              {
                if (!TheSpellCounts.MaxReceivedCounts.ContainsKey(spellId) || TheSpellCounts.MaxReceivedCounts[spellId] < data.TheSpellData.MaxReceivedCounts[spellId])
                {
                  TheSpellCounts.MaxReceivedCounts[spellId] = data.TheSpellData.MaxReceivedCounts[spellId];
                }
              }

              foreach (var spellData in data.TheSpellData.UniqueSpells.Keys)
              {
                if (!TheSpellCounts.UniqueSpells.ContainsKey(spellData))
                {
                  TheSpellCounts.UniqueSpells[spellData] = data.TheSpellData.UniqueSpells[spellData];
                }
              }

              if (SpellRowsView.Count > 0)
              {
                SpellRowsView.Clear();
              }

              OptionsChanged();
            }
          }
        }
      }
      catch (IOException ex)
      {
        LOG.Error(ex);
      }
      catch (UnauthorizedAccessException uax)
      {
        LOG.Error(uax);
      }
      catch (SecurityException se)
      {
        LOG.Error(se);
      }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
      try
      {
        var data = new SpellCountsSerialized { PlayerNames = PlayerList, TheSpellData = TheSpellCounts };
        var result = JsonConvert.SerializeObject(data);
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        string filter = "Spell Count File (*.scf.gz)|*.scf.gz";
        saveFileDialog.Filter = filter;
        if (saveFileDialog.ShowDialog().Value)
        {
          FileInfo gzipFileName = new FileInfo(saveFileDialog.FileName);
          using (FileStream gzipTargetAsStream = gzipFileName.Create())
          {
            using (GZipStream gzipStream = new GZipStream(gzipTargetAsStream, CompressionMode.Compress))
            {
              using (var writer = new StreamWriter(gzipStream))
              {
                writer.Write(result);
                writer.Close();
              }
            }
          }
        }
      }
      catch (IOException ex)
      {
        LOG.Error(ex);
      }
      catch (UnauthorizedAccessException uax)
      {
        LOG.Error(uax);
      }
      catch (SecurityException se)
      {
        LOG.Error(se);
      }
    }


    private Tuple<List<string>,List<List<string>>> BuildExportData()
    {
      List<string> header = new List<string>();
      List<List<string>> data = new List<List<string>>();

      header.Add("");
      for (int i = 2; i < dataGrid.Columns.Count; i++)
      {
        header.Add(dataGrid.Columns[i].Header as string);
      }

      foreach (var item in dataGrid.Items)
      {
        var counts = item as SpellCountRow;
        List<string> row = new List<string> { counts.Spell };
        foreach (var value in counts.Values)
        {
          row.Add(value.ToString(CultureInfo.CurrentCulture));
        }

        data.Add(row);
      }

      return new Tuple<List<string>, List<List<string>>>(header, data);
    }

    private void CopyCsvClick(object sender, RoutedEventArgs e)
    {
      try
      {
        var export = BuildExportData();
        string result = TextFormatUtils.BuildCsv(export.Item1, export.Item2, titleLabel.Content as string);
        Clipboard.SetDataObject(result);
      }
      catch (ArgumentNullException ane)
      {
        Clipboard.SetDataObject("EQ Log Parser Error: Failed to create BBCode\r\n");
        LOG.Error(ane);
      }
      catch (ExternalException ex)
      {
        LOG.Error(ex);
      }
    }

    private void CopyBBCodeClick(object sender, RoutedEventArgs e)
    {
      try
      {
        var export = BuildExportData();
        string result = TextFormatUtils.BuildBBCodeTable(export.Item1, export.Item2, titleLabel.Content as string);
        Clipboard.SetDataObject(result);
      }
      catch (ArgumentNullException ane)
      {
        Clipboard.SetDataObject("EQ Log Parser Error: Failed to create BBCode\r\n");
        LOG.Error(ane);
      }
      catch (ExternalException ex)
      {
        LOG.Error(ex);
      }
    }

    private void CopyGamparseClick(object sender, RoutedEventArgs e)
    {
      try
      {
        var export = BuildExportData();
        string result = TextFormatUtils.BuildGamparseList(export.Item1, export.Item2, titleLabel.Content as string);
        Clipboard.SetDataObject(result);
      }
      catch (ArgumentNullException ane)
      {
        Clipboard.SetDataObject("EQ Log Parser Error: Failed to create BBCode\r\n");
        LOG.Error(ane);
      }
      catch (ExternalException ex)
      {
        LOG.Error(ex);
      }
    }

    private void RemoveSelectedRowsClick(object sender, RoutedEventArgs e)
    {
      // Don't allow if the previous operation hasn't finished
      // this probably needs to be better...
      if (!Running)
      {
        var modified = false;
        while (dataGrid.SelectedItems.Count > 0)
        {
          if (dataGrid.SelectedItem is SpellCountRow spr)
          {
            HiddenSpells[spr.Spell] = 1;
            SpellRowsView.Remove(spr);
            modified = true;
          }
        }

        if (modified)
        {
          OptionsChanged();
        }
      }
    }

    private void RemoveSpellMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      var cell = sender as DataGridCell;

      // Don't allow if the previous operation hasn't finished
      // this probably needs to be better...
      if (!Running && cell.DataContext is SpellCountRow spr)
      {
        HiddenSpells[spr.Spell] = 1;
        SpellRowsView.Remove(spr);
        OptionsChanged();
      }
    }

    private void GridSizeChanged(object sender, SizeChangedEventArgs e)
    {
      var settingsLoc = settingsPanel.PointToScreen(new Point(0, 0));
      var titleLoc = titlePanel.PointToScreen(new Point(0, 0));

      if ((titleLoc.X + titlePanel.ActualWidth) > (settingsLoc.X + 10))
      {
        titlePanel.Visibility = Visibility.Hidden;
      }
      else
      {
        titlePanel.Visibility = Visibility.Visible;
      }
    }
  }
}
