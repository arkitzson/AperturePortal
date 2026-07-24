using System.Windows;
using System.Windows.Input;

namespace ApertureOS;

public partial class CategoryNameWindow : Window
{
    public string? ResultName { get; private set; }

    public CategoryNameWindow(string title, string initialName = "")
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        NameTextBox.Text = initialName;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a category name.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultName = name;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
