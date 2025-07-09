using System;

namespace Requestr.Core.Services;

public class ThemeService
{
    private bool _isDarkMode = false;
    
    public event Action? OnThemeChanged;
    
    public bool IsDarkMode => _isDarkMode;
    
    public void ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
        OnThemeChanged?.Invoke();
    }
    
    public void SetTheme(bool isDarkMode)
    {
        if (_isDarkMode != isDarkMode)
        {
            _isDarkMode = isDarkMode;
            OnThemeChanged?.Invoke();
        }
    }
    
    public string GetThemeClass()
    {
        return _isDarkMode ? "dark-theme" : "light-theme";
    }
}
