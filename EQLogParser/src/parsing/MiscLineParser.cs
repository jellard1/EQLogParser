﻿
using System;
using System.Collections.Generic;
using System.Linq;

namespace EQLogParser
{
  class MiscLineParser
  {
    private static readonly log4net.ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly DateUtil DateUtil = new DateUtil();
    private static readonly List<string> Currency = new List<string> { "Platinum", "Gold", "Silver", "Copper" };
    private static readonly Dictionary<char, uint> Rates = new Dictionary<char, uint>() { { 'p', 1000 }, { 'g', 100 }, { 's', 10 }, { 'c', 1 } };
    private static readonly char[] LootedFromTrim = new char[] { '-', '.' };

    public static void Process(LineData lineData)
    {
      try
      {
        string[] split = lineData.Action.Split(' ');

        if (split != null && split.Length > 2)
        {
          // [Sun Mar 01 22:20:36 2020] A shaded torch has been awakened by Drogbaa.
          // [Sun Mar 01 22:34:58 2020] You have entered The Eastern Wastes.
          // [Sun Mar 01 20:35:55 2020] The master looter, Qulas, looted 32426 platinum from the corpse.
          // [Sun Mar 01 23:51:02 2020] You receive 129 platinum, 2 gold and 1 copper as your split (with a lucky bonus).
          // [Sun Feb 02 22:43:51 2020] You receive 28 platinum, 7 gold, 2 silver and 5 copper as your split.
          // [Sun Feb 02 23:31:23 2020] You receive 57 platinum as your split.
          // [Fri Feb 07 22:01:20 2020] --Kizant has looted a Lesser Engraved Velium Rune from Velden Dragonbane's corpse.--
          // [Sat Feb 08 01:20:26 2020] --Proximoe has looted a Velium Infused Spider Silk from a restless devourer's corpse.--
          // [Sat Feb 08 21:21:36 2020] --You have looted a Cold-Forged Cudgel from Queen Dracnia's corpse.--

          string looter = null;
          int awakenedIndex = -1;
          int lootedIndex = -1;
          int masterLootIndex = -1;
          int receiveIndex = -1;
          bool handled = false;

          for (int i = 0; i < split.Length && !handled; i++)
          {
            if (i == 0 && split[0].StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
              looter = split[0] == "--You" ? ConfigUtil.PlayerName : split[0].TrimStart('-');
            }
            else
            {
              switch (split[i])
              {
                case "awakened":
                  awakenedIndex = i;
                  break;
                case "looted":
                  lootedIndex = i;
                  break;
                case "looter,":
                  masterLootIndex = (i == 2 && split[1] == "master" && split[0] == "The") ? masterLootIndex = i + 1 : -1;
                  break;
                case "receive":
                  receiveIndex = (i == 1 && split[0] == "You") ? i : -1;
                  break;
                case "by":
                  if (awakenedIndex > -1 && awakenedIndex == (i - 1) && split.Length > 5 && split[i - 2] == "been" && split[i - 3] == "has")
                  {
                     string awakened = string.Join(" ", split, 0, i - 3);
                    string breaker = string.Join(" ", split, i + 1, split.Length - i - 1).TrimEnd('.');
                    DataManager.Instance.AddMiscRecord(new MezBreakRecord() { Breaker = breaker, Awakened = awakened }, DateUtil.ParseLogDate(lineData.Line));
                    handled = true;
                  }
                  break;
                case "entered":
                  if (i == 2 && split[1] == "have" && split[0] == "You" && split.Length > 2)
                  {
                    string zone = string.Join(" ", split, 3, split.Length - 3).TrimEnd('.');
                    DataManager.Instance.AddMiscRecord(new ZoneRecord() { Zone = zone }, DateUtil.ParseLogDate(lineData.Line));
                    handled = true;
                  }
                  break;
                case "from":
                  if (masterLootIndex > -1 && lootedIndex > masterLootIndex && split.Length > lootedIndex + 1 && split.Length > 3)
                  {
                    string name = split[3].TrimEnd(',');
                    if (ParseCurrency(split, lootedIndex + 1, i, out string item, out uint count))
                    {
                      PlayerManager.Instance.AddVerifiedPlayer(name);
                      LootRecord record = new LootRecord() { Item = item, Player = name, Quantity = count, IsCurrency = true };
                      DataManager.Instance.AddLootRecord(record, DateUtil.ParseLogDate(lineData.Line));
                      handled = true;
                    }
                  }
                  else if (!string.IsNullOrEmpty(looter) && lootedIndex == 2 && split.Length > 4)
                  {
                    // covers "a" or "an"
                    uint count = split[3][0] == 'a' ? 1 : StatsUtil.ParseUInt(split[3]);
                    string item = string.Join(" ", split, 4, i - 4);
                    string npc = string.Join(" ", split, i + 1, split.Length - i - 1).TrimEnd(LootedFromTrim).Replace("'s corpse", "");

                    if (count > 0 && count != ushort.MaxValue)
                    {
                      PlayerManager.Instance.AddVerifiedPlayer(looter);
                      LootRecord record = new LootRecord() { Item = item, Player = looter, Quantity = count, IsCurrency = false, Npc = npc };
                      DataManager.Instance.AddLootRecord(record, DateUtil.ParseLogDate(lineData.Line));
                      handled = true;
                    }
                  }
                  break;
                case "split":
                  if (receiveIndex > -1 && split[i - 1] == "your" && split[i - 2] == "as")
                  {
                    if (ParseCurrency(split, 2, i - 2, out string item, out uint count))
                    {
                      LootRecord record = new LootRecord() { Item = item, Player = ConfigUtil.PlayerName, Quantity = count, IsCurrency = true };
                      DataManager.Instance.AddLootRecord(record, DateUtil.ParseLogDate(lineData.Line));
                      handled = true;
                    }
                  }
                  break;
              }
            }
          }
        }
      }
      catch (ArgumentNullException ne)
      {
        LOG.Error(ne);
      }
      catch (NullReferenceException nr)
      {
        LOG.Error(nr);
      }
      catch (ArgumentOutOfRangeException aor)
      {
        LOG.Error(aor);
      }
      catch (ArgumentException ae)
      {
        LOG.Error(ae);
      }
    }

    private static bool ParseCurrency(string[] pieces, int startIndex, int toIndex, out string item, out uint count)
    {
      bool parsed = true;
      item = null;
      count = 0;

      List<string> tmp = new List<string>();
      for (int i = startIndex; i < toIndex; i += 2)
      {
        if (pieces[i] == "and")
        {
          i -= 1;
          continue;
        }

        if (StatsUtil.ParseUInt(pieces[i]) is uint value && Currency.FirstOrDefault(curr => pieces[i + 1].StartsWith(curr, StringComparison.OrdinalIgnoreCase)) is string type)
        {
          tmp.Add(pieces[i] + " " + type);
          count += value * Rates[pieces[i + 1][0]];
        }
        else
        {
          parsed = false;
          break;
        }
      }

      if (parsed && tmp.Count > 0)
      {
        item = string.Join(", ", tmp);
      }

      return parsed && count != ushort.MaxValue;
    }
  }
}
