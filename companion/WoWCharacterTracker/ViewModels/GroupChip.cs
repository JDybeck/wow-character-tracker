using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// A filter chip above the character list: a realm (Retail) or ruleset (Forever), or "All".
sealed record GroupChip(string Key, string Label);
