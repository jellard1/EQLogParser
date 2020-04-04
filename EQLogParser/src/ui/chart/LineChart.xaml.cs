﻿using ActiproSoftware.Windows.Themes;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EQLogParser
{
  /// <summary>
  /// Interaction logic for DPSChart.xaml
  /// </summary>
  public partial class LineChart : UserControl
  {
    private static readonly log4net.ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    private static CartesianMapper<DataPoint> CONFIG_VPS = Mappers.Xy<DataPoint>()
     .X(dateModel => dateModel.CurrentTime)
     .Y(dateModel => dateModel.VPS);
    private static CartesianMapper<DataPoint> CONFIG_TOTAL = Mappers.Xy<DataPoint>()
     .X(dateModel => dateModel.CurrentTime)
     .Y(dateModel => dateModel.Total);
    private static CartesianMapper<DataPoint> CONFIG_CRIT_RATE = Mappers.Xy<DataPoint>()
     .X(dateModel => dateModel.CurrentTime)
     .Y(dateModel => dateModel.CritRate);
    private static CartesianMapper<DataPoint> CONFIG_AVG = Mappers.Xy<DataPoint>()
     .X(dateModel => dateModel.CurrentTime)
     .Y(dateModel => dateModel.Avg);

    private static List<CartesianMapper<DataPoint>> CHOICES = new List<CartesianMapper<DataPoint>>()
    {
      CONFIG_VPS, CONFIG_TOTAL, CONFIG_AVG, CONFIG_CRIT_RATE
    };

    private const string PETPLAYEROPTION = "Players +Pets";
    private const string PLAYEROPTION = "Players";
    private const string PETOPTION = "Pets";
    private const string RAIDOPTION = "Raid Totals";

    private DateTime ChartModifiedTime;
    private Dictionary<string, ChartValues<DataPoint>> PlayerPetValues = new Dictionary<string, ChartValues<DataPoint>>();
    private Dictionary<string, ChartValues<DataPoint>> PlayerValues = new Dictionary<string, ChartValues<DataPoint>>();
    private Dictionary<string, ChartValues<DataPoint>> PetValues = new Dictionary<string, ChartValues<DataPoint>>();
    private Dictionary<string, ChartValues<DataPoint>> RaidValues = new Dictionary<string, ChartValues<DataPoint>>();

    private CartesianMapper<DataPoint> CurrentConfig;
    private string CurrentPetOrPlayerOption;
    private List<PlayerStats> LastSelected = null;
    private Predicate<object> LastFilter = null;
    private List<ChartValues<DataPoint>> LastSortedValues = null;
    private Dictionary<string, Dictionary<string, byte>> HasPets = new Dictionary<string, Dictionary<string, byte>>();

    public LineChart(List<string> choices, bool includePets = false)
    {
      InitializeComponent();

      lvcChart.Hoverable = false;
      lvcChart.DisableAnimations = true;
      lvcChart.DataTooltip = null;

      // reverse regular tooltip
      //lvcChart.DataTooltip.Foreground = (SolidColorBrush) Application.Current.FindResource(AssetResourceKeys.ToolTipBackgroundNormalBrushKey);
      //lvcChart.DataTooltip.Background = (SolidColorBrush) Application.Current.FindResource(AssetResourceKeys.ToolTipForegroundNormalBrushKey);
      lvcChart.ChartLegend.Foreground = (SolidColorBrush)Application.Current.FindResource(AssetResourceKeys.ToolTipBackgroundNormalBrushKey);
      lvcChart.ChartLegend.Background = (SolidColorBrush)Application.Current.FindResource(AssetResourceKeys.ToolTipForegroundNormalBrushKey);

      CurrentConfig = CONFIG_VPS;
      choicesList.ItemsSource = choices;
      choicesList.SelectedIndex = 0;

      if (includePets)
      {
        petOrPlayerList.ItemsSource = new List<string> { PETPLAYEROPTION, PLAYEROPTION, PETOPTION, RAIDOPTION };
      }
      else
      {
        petOrPlayerList.ItemsSource = new List<string> { PLAYEROPTION, RAIDOPTION };
      }

      petOrPlayerList.SelectedIndex = 0;
      CurrentPetOrPlayerOption = petOrPlayerList.SelectedValue as string;

      Reset();
    }

    internal void Clear()
    {
      PlayerPetValues.Clear();
      PlayerValues.Clear();
      PetValues.Clear();
      RaidValues.Clear();
      HasPets.Clear();
      Reset();
    }

    internal void HandleUpdateEvent(DataPointEvent e)
    {
      switch (e.Action)
      {
        case "CLEAR":
          Clear();
          break;
        case "UPDATE":
          Clear();
          AddDataPoints(e.Iterator, e.Selected, e.Filter);
          break;
        case "SELECT":
          Plot(e.Selected);
          break;
        case "FILTER":
          Plot(e.Filter);
          break;
      }
    }

    internal void FixSize()
    {
      Task.Delay(750).ContinueWith(task =>
      {
        Dispatcher.InvokeAsync(() =>
        {
          lvcChart.UpdateLayout();
          lvcChart.Update();
        });
      }, TaskScheduler.Default);
    }

    private void AddDataPoints(RecordGroupCollection recordIterator, List<PlayerStats> selected = null, Predicate<object> filter = null)
    {
      DateTime newTaskTime = DateTime.Now;

      Task.Run(() =>
      {
        double lastTime = double.NaN;
        double firstTime = double.NaN;
        Dictionary<string, DataPoint> petData = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> playerData = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> totalPlayerData = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> raidData = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> needTotalAccounting = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> needPlayerAccounting = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> needPetAccounting = new Dictionary<string, DataPoint>();
        Dictionary<string, DataPoint> needRaidAccounting = new Dictionary<string, DataPoint>();

        foreach (var dataPoint in recordIterator)
        {
          double diff = double.IsNaN(lastTime) ? 1 : dataPoint.CurrentTime - lastTime;

          if (double.IsNaN(firstTime) || diff > DataManager.FIGHT_TIMEOUT)
          {
            firstTime = dataPoint.CurrentTime;
          }

          var raidName = "Raid";
          if (!raidData.TryGetValue(raidName, out DataPoint raidAggregate))
          {
            raidAggregate = new DataPoint() { Name = raidName };
            raidData[raidName] = raidAggregate;
          }

          Aggregate(raidData, RaidValues, needRaidAccounting, dataPoint, raidAggregate, firstTime, lastTime, diff);

          var playerName = dataPoint.PlayerName == null ? dataPoint.Name : dataPoint.PlayerName;
          var petName = dataPoint.PlayerName == null ? null : dataPoint.Name;
          var totalName = playerName + " +Pets";
          if (!totalPlayerData.TryGetValue(totalName, out DataPoint totalAggregate))
          {
            totalAggregate = new DataPoint() { Name = totalName, PlayerName = playerName };
            totalPlayerData[totalName] = totalAggregate;
          }

          Aggregate(totalPlayerData, PlayerPetValues, needTotalAccounting, dataPoint, totalAggregate, firstTime, lastTime, diff);

          if (dataPoint.PlayerName == null)
          {
            if (!playerData.TryGetValue(dataPoint.Name, out DataPoint aggregate))
            {
              aggregate = new DataPoint() { Name = dataPoint.Name };
              playerData[dataPoint.Name] = aggregate;
            }

            Aggregate(playerData, PlayerValues, needPlayerAccounting, dataPoint, aggregate, firstTime, lastTime, diff);
          }
          else if (dataPoint.PlayerName != null)
          {
            if (!HasPets.ContainsKey(totalName))
            {
              HasPets[totalName] = new Dictionary<string, byte>();
            }

            HasPets[totalName][petName] = 1;
            if (!petData.TryGetValue(petName, out DataPoint petAggregate))
            {
              petAggregate = new DataPoint() { Name = petName };
              petData[petName] = petAggregate;
            }

            Aggregate(petData, PetValues, needPetAccounting, dataPoint, petAggregate, firstTime, lastTime, diff);
          }

          lastTime = dataPoint.CurrentTime;
        }

        UpdateRemaining(RaidValues, needRaidAccounting, firstTime, lastTime);
        UpdateRemaining(PlayerPetValues, needTotalAccounting, firstTime, lastTime);
        UpdateRemaining(PlayerValues, needPlayerAccounting, firstTime, lastTime);
        UpdateRemaining(PetValues, needPetAccounting, firstTime, lastTime);
        Plot(newTaskTime, selected, filter);
      });
    }

    private void Aggregate(Dictionary<string, DataPoint> playerData, Dictionary<string, ChartValues<DataPoint>> theValues,
      Dictionary<string, DataPoint> needAccounting, DataPoint dataPoint, DataPoint aggregate, double firstTime, double lastTime, double diff)
    {
      if (diff > DataManager.FIGHT_TIMEOUT)
      {
        UpdateRemaining(theValues, needAccounting, firstTime, lastTime);
        foreach (var value in playerData.Values)
        {
          value.RollingTotal = 0;
          value.RollingCritHits = 0;
          value.RollingHits = 0;
          value.CurrentTime = lastTime + 6;
          Insert(value, theValues);
          value.CurrentTime = firstTime - 6;
          Insert(value, theValues);
        }
      }

      aggregate.Total += dataPoint.Total;
      aggregate.RollingTotal += dataPoint.Total;
      aggregate.RollingHits += 1;
      aggregate.RollingCritHits += LineModifiersParser.IsCrit(dataPoint.ModifiersMask) ? (uint)1 : 0;
      aggregate.BeginTime = firstTime;
      aggregate.CurrentTime = dataPoint.CurrentTime;

      if (diff >= 1)
      {
        Insert(aggregate, theValues);
        UpdateRemaining(theValues, needAccounting, firstTime, dataPoint.CurrentTime, aggregate.Name);
      }
      else
      {
        needAccounting[aggregate.Name] = aggregate;
      }
    }

    private void Plot(DateTime requestTime, List<PlayerStats> selected = null, Predicate<object> filter = null)
    {
      LastFilter = filter;
      LastSelected = selected;

      Dictionary<string, ChartValues<DataPoint>> workingData = null;

      string selectedLabel = "Selected Player(s)";
      string nonSelectedLabel = " Player(s)";
      switch (CurrentPetOrPlayerOption)
      {
        case PETPLAYEROPTION:
          workingData = PlayerPetValues;
          selectedLabel = "Selected Player +Pets(s)";
          nonSelectedLabel = " Player +Pets(s)";
          break;
        case PLAYEROPTION:
          workingData = PlayerValues;
          break;
        case PETOPTION:
          workingData = PetValues;
          selectedLabel = "Selected Pet(s)";
          nonSelectedLabel = " Pet(s)";
          break;
        case RAIDOPTION:
          workingData = RaidValues;
          break;
        default:
          workingData = new Dictionary<string, ChartValues<DataPoint>>();
          break;
      }

      string label;
      List<ChartValues<DataPoint>> sortedValues;
      if (CurrentPetOrPlayerOption == RAIDOPTION)
      {
        sortedValues = workingData.Values.ToList();
        label = sortedValues.Count > 0 ? "Raid Totals" : Labels.NODATA;
      }
      else if (selected == null || selected.Count == 0)
      {
        sortedValues = workingData.Values.Where(values => PassFilter(filter, values)).OrderByDescending(values => values.Last().Total).Take(5).ToList();
        label = sortedValues.Count > 0 ? "Top " + sortedValues.Count + nonSelectedLabel : Labels.NODATA;
      }
      else
      {
        List<string> names = selected.Select(stats => stats.OrigName).ToList();
        sortedValues = workingData.Values.Where(values =>
        {
          bool pass = false;
          var first = values.First();
          if (CurrentPetOrPlayerOption == PETPLAYEROPTION)
          {
            pass = names.Contains(first.PlayerName) || (HasPets.ContainsKey(first.Name) && names.FirstOrDefault(name => HasPets[first.Name].ContainsKey(name)) != null);
          }
          else
          {
            pass = names.Contains(first.Name);
          }
          return pass;
        }).Take(10).ToList();

        label = sortedValues.Count > 0 ? selectedLabel : Labels.NODATA;
      }

      LastSortedValues = sortedValues = Smoothing(sortedValues);

      Dispatcher.InvokeAsync(() =>
      {
        if (ChartModifiedTime < requestTime)
        {
          ChartModifiedTime = requestTime;
          Reset();

          titleLabel.Content = label;
          SeriesCollection collection = new SeriesCollection(CurrentConfig);
          bool fixStillNeeded = true;

          foreach (var value in sortedValues)
          {
            var name = value.First().Name;
            name = CurrentPetOrPlayerOption == PETPLAYEROPTION && !HasPets.ContainsKey(name) ? name.Split(' ')[0] : name;
            var series = new LineSeries() { Title = name, Values = value };

            if (value.Count > 1)
            {
              series.PointGeometry = null;
              fixStillNeeded = false;
            }
            else if (value.Count == 1 && fixStillNeeded) // handles if everything is 1 point
            {
              if (!double.IsNaN(lvcChart.AxisX[0].MinValue))
              {
                lvcChart.AxisX[0].MinValue = Math.Min(lvcChart.AxisX[0].MinValue, value[0].CurrentTime - 3.0);
              }
              else
              {
                lvcChart.AxisX[0].MinValue = value[0].CurrentTime - 3.0;
              }

              if (!double.IsNaN(lvcChart.AxisX[0].MaxValue))
              {
                lvcChart.AxisX[0].MaxValue = Math.Max(lvcChart.AxisX[0].MaxValue, value[0].CurrentTime + 3.0);
              }
              else
              {
                lvcChart.AxisX[0].MaxValue = value[0].CurrentTime + 3.0;
              }
            }
            else
            {
              fixStillNeeded = false;
            }

            if (!fixStillNeeded)
            {
              lvcChart.AxisX[0].MinValue = double.NaN;
              lvcChart.AxisX[0].MaxValue = double.NaN;
            }

            collection.Add(series);
          }

          lvcChart.Series = collection;
        }
      });
    }

    private bool PassFilter(Predicate<object> filter, ChartValues<DataPoint> values)
    {
      bool pass = filter == null;

      if (!pass)
      {
        var first = values.First();
        if (CurrentPetOrPlayerOption == PETPLAYEROPTION)
        {
          pass = filter(first.PlayerName) || (HasPets.ContainsKey(first.Name) && filter(HasPets[first.Name]));
        }
        else
        {
          pass = filter(first.Name);
        }
      }

      return pass;
    }

    private void Plot(Predicate<object> filter)
    {
      if (RaidValues.Count > 0)
      {
        Plot(DateTime.Now, LastSelected, filter);
      }
      else
      {
        Reset();
      }
    }

    private void Plot(List<PlayerStats> selected)
    {
      if (RaidValues.Count > 0)
      {
        // handling case where chart can be updated twice
        // when toggling bane and selection is lost
        if (!(selected.Count == 0 && LastSelected == null))
        {
          Plot(DateTime.Now, selected, LastFilter);
        }
      }
      else
      {
        Reset();
      }
    }

    private string GetLabelFormat(double value)
    {
      string dateTimeString;
      DateTime dt = value > 0 ? new DateTime((long)(value * TimeSpan.FromSeconds(1).Ticks)) : new DateTime();
      dateTimeString = dt.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
      return dateTimeString;
    }

    private void Reset()
    {
      if (lvcChart.Series != null)
      {
        Helpers.ChartResetView(lvcChart);
        lvcChart.AxisX[0].LabelFormatter = GetLabelFormat;
        lvcChart.Series = null;
        titleLabel.Content = Labels.NODATA;
      }
    }

    private void ChartDoubleClick(object sender, MouseButtonEventArgs e)
    {
      Helpers.ChartResetView(lvcChart);
    }

    private void ListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (PlayerPetValues.Count > 0)
      {
        CurrentConfig = CHOICES[choicesList.SelectedIndex];
        CurrentPetOrPlayerOption = petOrPlayerList.SelectedValue as string;
        Plot(DateTime.Now, LastSelected, LastFilter);
      }
    }

    private void SaveCSVClick(object sender, RoutedEventArgs e)
    {
      if (LastSortedValues != null)
      {
        try
        {
          StringBuilder sb = new StringBuilder();
          sb.Append("Seconds,").Append(choicesList.SelectedValue as string).Append(",Name").AppendLine();

          LastSortedValues.Where(values => LastFilter == null || LastFilter(values.First())).ToList().ForEach(sortedValue =>
          {
            foreach (var chartData in sortedValue)
            {
              double chartValue = 0;
              if (CurrentConfig == CONFIG_AVG)
              {
                chartValue = chartData.Avg;
              }
              else if (CurrentConfig == CONFIG_CRIT_RATE)
              {
                chartValue = chartData.CritRate;
              }
              else if (CurrentConfig == CONFIG_TOTAL)
              {
                chartValue = chartData.Total;
              }
              else if (CurrentConfig == CONFIG_VPS)
              {
                chartValue = chartData.VPS;
              }

              sb.Append(chartData.CurrentTime).Append(",").Append(chartValue).Append(",").Append(chartData.Name).AppendLine();
            }
          });

          SaveFileDialog saveFileDialog = new SaveFileDialog();
          string filter = "CSV file (*.csv)|*.csv";
          saveFileDialog.Filter = filter;
          if (saveFileDialog.ShowDialog().Value)
          {
            File.WriteAllText(saveFileDialog.FileName, sb.ToString());
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
    }

    private static void UpdateRemaining(Dictionary<string, ChartValues<DataPoint>> chartValues, Dictionary<string, DataPoint> needAccounting,
      double firstTime, double currentTime, string ignore = null)
    {
      foreach (var remaining in needAccounting.Values)
      {
        if (ignore != remaining.Name)
        {
          if (remaining.BeginTime != firstTime)
          {
            remaining.BeginTime = firstTime;
            remaining.RollingTotal = 0;
            remaining.RollingHits = 0;
            remaining.RollingCritHits = 0;
          }

          remaining.CurrentTime = currentTime;
          Insert(remaining, chartValues);
        }
      }

      needAccounting.Clear();
    }

    private static void Insert(DataPoint aggregate, Dictionary<string, ChartValues<DataPoint>> chartValues)
    {
      DataPoint newEntry = new DataPoint
      {
        Name = aggregate.Name,
        PlayerName = aggregate.PlayerName,
        CurrentTime = aggregate.CurrentTime,
        Total = aggregate.Total
      };

      double totalSeconds = aggregate.CurrentTime - aggregate.BeginTime + 1;
      newEntry.VPS = (long)Math.Round(aggregate.RollingTotal / totalSeconds, 2);

      if (aggregate.RollingHits > 0)
      {
        newEntry.Avg = (long)Math.Round(Convert.ToDecimal(aggregate.RollingTotal) / aggregate.RollingHits, 2);
        newEntry.CritRate = Math.Round(Convert.ToDouble(aggregate.RollingCritHits) / aggregate.RollingHits * 100, 2);
      }

      if (!chartValues.TryGetValue(aggregate.Name, out ChartValues<DataPoint> playerValues))
      {
        playerValues = new ChartValues<DataPoint>();
        chartValues[aggregate.Name] = playerValues;
      }

      DataPoint test;
      if (playerValues.Count > 0 && (test = playerValues.Last()) != null && test.CurrentTime == newEntry.CurrentTime)
      {
        playerValues[playerValues.Count - 1] = newEntry;
      }
      else
      {
        playerValues.Add(newEntry);
      }
    }

    private static List<ChartValues<DataPoint>> Smoothing(List<ChartValues<DataPoint>> data)
    {
      List<ChartValues<DataPoint>> smoothed = new List<ChartValues<DataPoint>>();

      data.ForEach(points =>
      {
        if (points.Count > 750)
        {
          int tries = 1;
          int rate = 0;
          var current = points;
          ChartValues<DataPoint> updatedValues;

          do
          {
            updatedValues = new ChartValues<DataPoint>();
            rate += (6 * tries);

            for (int i = 0; i < current.Count - 2; i++)
            {
              var one = current[i];
              var two = current[i + 1];
              var three = current[i + 2];

              if (two.CurrentTime - one.CurrentTime <= rate && three.CurrentTime - one.CurrentTime <= rate)
              {
                one.CurrentTime = Math.Truncate((one.CurrentTime + two.CurrentTime + three.CurrentTime) / 3);
                one.Total = (one.Total + two.Total + three.Total) / 3;
                one.VPS = (one.VPS + two.VPS + three.VPS) / 3;
                one.Avg = (one.Avg + two.Avg + three.Avg) / 3;
                one.CritRate = (one.CritRate + two.CritRate + three.CritRate) / 3;
                updatedValues.Add(one);
                i += 2;
              }
              else
              {
                updatedValues.Add(one);
                updatedValues.Add(two);
                i += 1;
              }
            }

            current = updatedValues;
          }
          while (++tries < 12 && updatedValues.Count > 750);

          smoothed.Add(updatedValues);
        }
        else
        {
          smoothed.Add(points);
        }
      });

      return smoothed;
    }
  }
}
