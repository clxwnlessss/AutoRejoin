using System.Collections.Generic;
using System.ComponentModel;

namespace AutoRejoinApp;

public abstract class Notify : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler PropertyChanged;

	protected void Changed(params string[] names)
	{
		PropertyChangedEventHandler propertyChanged = this.PropertyChanged;
		if (propertyChanged != null)
		{
			foreach (string propertyName in names)
			{
				propertyChanged(this, new PropertyChangedEventArgs(propertyName));
			}
		}
	}

	protected bool Set<T>(ref T field, T value, params string[] names)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		Changed(names);
		return true;
	}
}
