using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CSharpReplLib.WpfSample
{
	public class ScriptResultTemplateSelector : DataTemplateSelector
	{
		public override DataTemplate SelectTemplate(object item, DependencyObject container)
		{
			FrameworkElement element = container as FrameworkElement;

			if (element != null && item != null && item is ScriptHandler.ScriptResult result)
			{
				if (result.IsError)
					return element.FindResource("ScriptResultErrorTemplate") as DataTemplate;
				else if (result.IsCancelled)
					return element.FindResource("ScriptResultCancelledTemplate") as DataTemplate;
				else if (result.ReturnedValue != null)
					return element.FindResource("ScriptResultValueTemplate") as DataTemplate;
				else
					return element.FindResource("ScriptResultResultTemplate") as DataTemplate;
			}

			return null;
		}
	}
}
