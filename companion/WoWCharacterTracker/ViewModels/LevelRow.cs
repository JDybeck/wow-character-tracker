using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

sealed record LevelRow(string LevelText, string Took, string Zone, string When);
