﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EQLogParser
{
  public class SummaryTable : UserControl
  {
    internal const string DEFAULT_TABLE_LABEL = "No NPCs Selected";
    internal const string NODATA_TABLE_LABEL = Labels.NODATA;

    internal event EventHandler<PlayerStatsSelectionChangedEventArgs> EventsSelectionChange;

    internal DataGrid TheDataGrid;
    internal Label TheTitle;
    internal CombinedStats CurrentStats;
    internal List<List<ActionBlock>> CurrentGroups;

    internal void InitSummaryTable(Label title, DataGrid dataGrid)
    {
      TheDataGrid = dataGrid;
      TheTitle = title;

      if (title != null)
      {
        TheTitle.Content = DEFAULT_TABLE_LABEL;
      }

      if (TheDataGrid != null)
      {
        TheDataGrid.Sorting += DataGrid_Sorting; // sort numbers descending
      }
    }

    internal static void CreateClassMenuItems(MenuItem parent, Action<object, RoutedEventArgs> selectedHandler, Action<object, RoutedEventArgs> classHandler)
    {
      MenuItem selected = new MenuItem() { IsEnabled = false, Header = "Selected" };
      selected.Click += new RoutedEventHandler(selectedHandler);
      parent.Items.Add(selected);

      PlayerManager.Instance.GetClassList().ForEach(name =>
      {
        MenuItem item = new MenuItem() { IsEnabled = false, Header = name };
        item.Click += new RoutedEventHandler(classHandler);
        parent.Items.Add(item);
      });
    }

    internal void Clear()
    {
      TheTitle.Content = DEFAULT_TABLE_LABEL;
      TheDataGrid.ItemsSource = null;
    }

    internal static void EnableClassMenuItems(MenuItem menu, DataGrid dataGrid, Dictionary<string, byte> uniqueClasses)
    {
      foreach (var item in menu.Items)
      {
        MenuItem menuItem = item as MenuItem;
        menuItem.IsEnabled = menuItem.Header as string == "Selected" ? dataGrid.SelectedItems.Count > 0 : uniqueClasses != null && uniqueClasses.ContainsKey(menuItem.Header as string);
      }
    }

    internal Predicate<object> GetFilter()
    {
      return (TheDataGrid.ItemsSource as ICollectionView)?.Filter;
    }

    internal List<string[]> GetHeaders()
    {
      return TheDataGrid.Columns.Select(column =>
      {
        string binding = "";
        string title = "";
        if (column is DataGridTextColumn textColumn && textColumn.Binding is System.Windows.Data.Binding theBinding)
        {
          title = textColumn.Header as string;
          binding = theBinding.Path.Path;
        }

        return new string[] { binding, title };
      }).ToList();
    }

    internal List<PlayerStats> GetPlayerStats()
    {
      var results = new List<PlayerStats>();
      if (TheDataGrid.ItemsSource != null)
      {
        foreach (var item in TheDataGrid.ItemsSource as ICollectionView)
        {
          if (item is PlayerStats stats)
          {
            results.Add(stats);
            if (CurrentStats.Children.ContainsKey(stats.Name))
            {
              results.AddRange(CurrentStats.Children[stats.Name]);
            }
          }
        }
      }

      return results;
    }

    internal string GetTargetTitle()
    {
      return CurrentStats?.TargetTitle ?? GetTitle();
    }

    internal string GetTitle()
    {
      return TheTitle.Content as string;
    }

    internal List<PlayerStats> GetSelectedStats()
    {
      return TheDataGrid.SelectedItems.Cast<PlayerStats>().ToList();
    }

    internal void DataGridSelectAllClick(object sender, RoutedEventArgs e)
    {
      Helpers.DataGridSelectAll(sender as FrameworkElement);
    }

    internal void DataGridUnselectAllClick(object sender, RoutedEventArgs e)
    {
      Helpers.DataGridUnselectAll(sender as FrameworkElement);
    }

    internal void DataGridShowBreakdownClick(object sender, RoutedEventArgs e)
    {
      ShowBreakdown(GetSelectedStats());
    }

    internal void DataGridShowBreakdown2Click(object sender, RoutedEventArgs e)
    {
      ShowBreakdown2(GetSelectedStats());
    }

    internal void DataGridShowBreakdownByClassClick(object sender, RoutedEventArgs e)
    {
      MenuItem menuItem = (sender as MenuItem);
      ShowBreakdown(GetPlayerStatsByClass(menuItem?.Header as string));
    }

    internal void DataGridShowBreakdown2ByClassClick(object sender, RoutedEventArgs e)
    {
      MenuItem menuItem = sender as MenuItem;
      ShowBreakdown2(GetPlayerStatsByClass(menuItem?.Header as string));
    }

    internal void DataGridShowSpellCastsClick(object sender, RoutedEventArgs e)
    {
      ShowSpellCasts(GetSelectedStats());
    }

    internal void DataGridSpellCastsByClassClick(object sender, RoutedEventArgs e)
    {
      MenuItem menuItem = (sender as MenuItem);
      ShowSpellCasts(GetPlayerStatsByClass(menuItem?.Header as string));
    }

    internal List<PlayerStats> GetPlayerStatsByClass(string className)
    {
      List<PlayerStats> selectedStats = new List<PlayerStats>();
      foreach (var item in TheDataGrid.Items)
      {
        PlayerStats stats = item as PlayerStats;
        if (stats.ClassName == className)
        {
          selectedStats.Add(stats);
        }
      }

      return selectedStats;
    }

    internal void SetPetClick(object sender, RoutedEventArgs e)
    {
      ContextMenu menu = (sender as FrameworkElement)?.Parent as ContextMenu;
      DataGrid callingDataGrid = menu?.PlacementTarget as DataGrid;
      if (callingDataGrid.SelectedItem is PlayerStats stats)
      {
        Task.Delay(150).ContinueWith(_ =>
        {
          PlayerManager.Instance.AddVerifiedPet(stats.OrigName);
          PlayerManager.Instance.AddPetToPlayer(stats.OrigName, Labels.UNASSIGNED);
        }, TaskScheduler.Default);
      }
    }

    internal void FireSelectionChangedEvent(List<PlayerStats> selected)
    {
      var selectionChanged = new PlayerStatsSelectionChangedEventArgs();
      selectionChanged.Selected.AddRange(selected);
      selectionChanged.CurrentStats = CurrentStats;
      EventsSelectionChange(this, selectionChanged);
    }

    internal virtual void ShowBreakdown(List<PlayerStats> selected)
    {
      // need to override this method
    }

    internal virtual void ShowBreakdown2(List<PlayerStats> selected)
    {
      // need to override this method
    }

    internal void ShowSpellCasts(List<PlayerStats> selected)
    {
      if (selected?.Count > 0)
      {
        var spellTable = new SpellCountTable(CurrentStats?.ShortTitle ?? "");
        spellTable.ShowSpells(selected, CurrentStats);
        var main = Application.Current.MainWindow as MainWindow;
        Helpers.OpenNewTab(main.dockSite, "spellCastsWindow", "Spell Counts", spellTable);
      }
    }

    private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
      if (e.Column.Header != null && e.Column.Header.ToString() != "Name")
      {
        e.Column.SortDirection = e.Column.SortDirection ?? ListSortDirection.Ascending;
      }
    }
  }
}
