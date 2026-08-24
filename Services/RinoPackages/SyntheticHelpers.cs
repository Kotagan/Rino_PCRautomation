using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[CompilerGenerated]
internal sealed class _003C_003Ez__ReadOnlyArray<T> : IEnumerable, ICollection, IList, IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, ICollection<T>, IList<T>
{
	private readonly T[] _items;

	int ICollection.Count => _items.Length;

	bool ICollection.IsSynchronized => false;

	object ICollection.SyncRoot => this;

	object? IList.this[int index]
	{
		get => _items[index];
		set => throw new NotSupportedException();
	}

	bool IList.IsFixedSize => true;

	bool IList.IsReadOnly => true;

	int IReadOnlyCollection<T>.Count => _items.Length;

	T IReadOnlyList<T>.this[int index] => _items[index];

	int ICollection<T>.Count => _items.Length;

	bool ICollection<T>.IsReadOnly => true;

	T IList<T>.this[int index]
	{
		get => _items[index];
		set => throw new NotSupportedException();
	}

	public _003C_003Ez__ReadOnlyArray(T[] items)
	{
		_items = items;
	}

	public IEnumerator<T> GetEnumerator()
	{
		return ((IEnumerable<T>)_items).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _items.GetEnumerator();
	}

	void ICollection<T>.Add(T item) => throw new NotSupportedException();
	void ICollection<T>.Clear() => throw new NotSupportedException();
	bool ICollection<T>.Contains(T item) => Array.IndexOf(_items, item) >= 0;
	void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
	bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
	int IList<T>.IndexOf(T item) => Array.IndexOf(_items, item);
	void IList<T>.Insert(int index, T item) => throw new NotSupportedException();
	void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
	int IList.Add(object? value) => throw new NotSupportedException();
	void IList.Clear() => throw new NotSupportedException();
	bool IList.Contains(object? value) => ((IList)_items).Contains(value);
	int IList.IndexOf(object? value) => ((IList)_items).IndexOf(value);
	void IList.Insert(int index, object? value) => throw new NotSupportedException();
	void IList.Remove(object? value) => throw new NotSupportedException();
	void IList.RemoveAt(int index) => throw new NotSupportedException();
	void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
}

[CompilerGenerated]
internal sealed class _003C_003Ez__ReadOnlySingleElementList<T> : IEnumerable, ICollection, IList, IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, ICollection<T>, IList<T>
{
	private readonly T _item;

	int ICollection.Count => 1;

	bool ICollection.IsSynchronized => false;

	object ICollection.SyncRoot => this;

	object? IList.this[int index]
	{
		get => index == 0 ? _item : throw new ArgumentOutOfRangeException(nameof(index));
		set => throw new NotSupportedException();
	}

	bool IList.IsFixedSize => true;

	bool IList.IsReadOnly => true;

	int IReadOnlyCollection<T>.Count => 1;

	T IReadOnlyList<T>.this[int index] => index == 0 ? _item : throw new ArgumentOutOfRangeException(nameof(index));

	int ICollection<T>.Count => 1;

	bool ICollection<T>.IsReadOnly => true;

	T IList<T>.this[int index]
	{
		get => index == 0 ? _item : throw new ArgumentOutOfRangeException(nameof(index));
		set => throw new NotSupportedException();
	}

	public _003C_003Ez__ReadOnlySingleElementList(T item)
	{
		_item = item;
	}

	public IEnumerator<T> GetEnumerator()
	{
		yield return _item;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	void ICollection<T>.Add(T item) => throw new NotSupportedException();
	void ICollection<T>.Clear() => throw new NotSupportedException();
	bool ICollection<T>.Contains(T item) => EqualityComparer<T>.Default.Equals(_item, item);
	void ICollection<T>.CopyTo(T[] array, int arrayIndex) => array[arrayIndex] = _item;
	bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
	int IList<T>.IndexOf(T item) => EqualityComparer<T>.Default.Equals(_item, item) ? 0 : -1;
	void IList<T>.Insert(int index, T item) => throw new NotSupportedException();
	void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
	int IList.Add(object? value) => throw new NotSupportedException();
	void IList.Clear() => throw new NotSupportedException();
	bool IList.Contains(object? value) => value is T t && EqualityComparer<T>.Default.Equals(_item, t);
	int IList.IndexOf(object? value) => value is T t && EqualityComparer<T>.Default.Equals(_item, t) ? 0 : -1;
	void IList.Insert(int index, object? value) => throw new NotSupportedException();
	void IList.Remove(object? value) => throw new NotSupportedException();
	void IList.RemoveAt(int index) => throw new NotSupportedException();
	void ICollection.CopyTo(Array array, int index) => array.SetValue(_item, index);
}
