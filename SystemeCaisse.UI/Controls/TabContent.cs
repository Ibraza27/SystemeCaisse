using System;
using System.Windows;
using System.Windows.Controls;

namespace SystemeCaisse.UI.Controls
{
    /// <summary>
    /// Attached property permettant à un TabControl de conserver les vues de ses onglets
    /// en mémoire dans le visual tree (Visibility = Visible / Collapsed).
    /// Empêche la destruction/recréation des contrôles à chaque changement d'onglet,
    /// rendant le passage d'un onglet à l'autre ultra-fluide et instantané.
    /// </summary>
    public static class TabContent
    {
        public static bool GetIsCached(DependencyObject obj) => (bool)obj.GetValue(IsCachedProperty);
        public static void SetIsCached(DependencyObject obj, bool value) => obj.SetValue(IsCachedProperty, value);

        public static readonly DependencyProperty IsCachedProperty =
            DependencyProperty.RegisterAttached("IsCached", typeof(bool), typeof(TabContent),
                new FrameworkPropertyMetadata(false, OnIsCachedChanged));

        private static readonly DependencyProperty ItemsHolderProperty =
            DependencyProperty.RegisterAttached("ItemsHolder", typeof(Panel), typeof(TabContent),
                new FrameworkPropertyMetadata(null));

        private static void OnIsCachedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TabControl tabControl) return;

            if ((bool)e.NewValue)
            {
                tabControl.Loaded += TabControl_Loaded;
                tabControl.SelectionChanged += TabControl_SelectionChanged;
            }
            else
            {
                tabControl.Loaded -= TabControl_Loaded;
                tabControl.SelectionChanged -= TabControl_SelectionChanged;
            }
        }

        private static void TabControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TabControl tabControl) return;
            EnsureItemsHolder(tabControl);
            UpdateTabs(tabControl);
        }

        private static void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not TabControl tabControl) return;
            if (e.OriginalSource != tabControl) return;
            UpdateTabs(tabControl);
        }

        private static Panel? EnsureItemsHolder(TabControl tabControl)
        {
            var holder = (Panel?)tabControl.GetValue(ItemsHolderProperty);
            if (holder != null) return holder;

            var cp = FindChild<ContentPresenter>(tabControl, "PART_SelectedContentHost");
            if (cp == null) return null;

            if (cp.Parent is Decorator decorator)
            {
                var grid = new Grid();
                decorator.Child = grid;
                tabControl.SetValue(ItemsHolderProperty, grid);
                return grid;
            }
            else if (cp.Parent is Panel parentPanel)
            {
                int index = parentPanel.Children.IndexOf(cp);
                parentPanel.Children.RemoveAt(index);
                var grid = new Grid();
                parentPanel.Children.Insert(index, grid);
                tabControl.SetValue(ItemsHolderProperty, grid);
                return grid;
            }

            return null;
        }

        private static void UpdateTabs(TabControl tabControl)
        {
            var holder = EnsureItemsHolder(tabControl);
            if (holder == null) return;

            for (int i = 0; i < tabControl.Items.Count; i++)
            {
                var item = tabControl.Items[i];
                var tabItem = tabControl.ItemContainerGenerator.ContainerFromIndex(i) as TabItem 
                              ?? item as TabItem;

                if (tabItem == null) continue;

                ContentPresenter? tabContentPresenter = null;
                if (i < holder.Children.Count)
                {
                    tabContentPresenter = holder.Children[i] as ContentPresenter;
                }
                else
                {
                    tabContentPresenter = new ContentPresenter();
                    holder.Children.Add(tabContentPresenter);
                }

                if (tabContentPresenter.Content != tabItem.Content)
                {
                    tabContentPresenter.Content = tabItem.Content;
                    tabContentPresenter.ContentTemplate = tabItem.ContentTemplate ?? tabControl.ContentTemplate;
                }

                tabContentPresenter.Visibility = (i == tabControl.SelectedIndex) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }

            while (holder.Children.Count > tabControl.Items.Count)
            {
                holder.Children.RemoveAt(holder.Children.Count - 1);
            }
        }

        private static T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && (string.IsNullOrEmpty(childName) || (child as FrameworkElement)?.Name == childName))
                {
                    return typedChild;
                }

                var found = FindChild<T>(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
