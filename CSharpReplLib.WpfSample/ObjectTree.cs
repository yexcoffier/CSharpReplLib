using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CSharpReplLib.WpfSample
{
	public class ObjectTree : NotifyPropertyChanged, IDisposable
	{
		public string Name { get; }

		public Type Type { get; }
		public object Value { get; }
		public ObjectTree Parent { get; }
		public ObservableCollection<ObjectTree> Children { get; } = new ObservableCollection<ObjectTree>();

		readonly private bool _canExpand;


		private bool _isExpanded = false;
		public bool IsExpanded
		{
			get => _isExpanded;
			set => Set(ref _isExpanded, value);
		}

		public ObjectTree(ObjectTree parent, object value, string name)
		{
			Parent = parent;
			Value = value;
			Name = name;
			Type = value?.GetType();

			_canExpand = !(Value == null || Type == typeof(string) || Type.IsValueType);

			if (parent != null)
				parent.PropertyChanged += ObjectTree_PropertyChanged;
		}

		private void ObjectTree_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IsExpanded))
				Deploy();
		}

		public void Deploy()
		{
			if (!_canExpand || Value == null || Children.Any())
				return;

			if (Value is IEnumerable enumerable)
			{
				foreach (var child in DeployEnumerable(enumerable).Take(50))
					Children.Add(child);

				return;
			}

			var properties = Type.GetProperties().Select(GetObjectTreeFromProperty).Where(o => o != null);
			var fields = Type.GetFields().Select(field => new ObjectTree(this, field.GetValue(Value), field.Name));

			foreach (var child in properties.Concat(fields).OrderBy(ot => ot.Name))
				Children.Add(child);
		}

		private ObjectTree GetObjectTreeFromProperty(PropertyInfo prop)
		{
			try
			{
				var value = prop.GetValue(Value);
				return new ObjectTree(this, value, prop.Name);
			}
			catch (TargetInvocationException)
			{
				return null;
			}
		}

		private IEnumerable<ObjectTree> DeployEnumerable(IEnumerable enumerable)
		{
			var enumerator = enumerable.GetEnumerator();
			while (enumerator.MoveNext())
				yield return new ObjectTree(this, enumerator.Current, null);
		}

		public void Dispose() => throw new NotImplementedException();
	}
}
