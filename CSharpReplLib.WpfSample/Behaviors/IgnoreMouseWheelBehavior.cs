using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CSharpReplLib.WpfSample.Behaviors
{
	public class IgnoreMouseWheelBehavior
	{
		public static readonly DependencyProperty IgnoreMouseWheelProperty =
			DependencyProperty.RegisterAttached("IgnoreMouseWheel", typeof(bool),
			typeof(IgnoreMouseWheelBehavior), new UIPropertyMetadata(false, IgnoreMouseWheelChanged));

		public static bool GetIgnoreMouseWheel(FrameworkElement frameworkElement)
		{
			return (bool)frameworkElement.GetValue(IgnoreMouseWheelProperty);
		}

		public static void SetIgnoreMouseWheel(FrameworkElement frameworkElement, bool value)
		{
			frameworkElement.SetValue(IgnoreMouseWheelProperty, value);
		}

		static void IgnoreMouseWheelChanged(DependencyObject depObj, DependencyPropertyChangedEventArgs e)
		{
			if (!(depObj is FrameworkElement element))
				return;

			if ((bool)e.NewValue)
				element.PreviewMouseWheel += OnPreviewMouseWheel;
			else
				element.PreviewMouseWheel -= OnPreviewMouseWheel;
		}

		static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
		{
			e.Handled = true;

			var e2 = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
			{
				RoutedEvent = UIElement.MouseWheelEvent
			};

			if (sender is FrameworkElement element)
				element.RaiseEvent(e2);
		}

	}
}
