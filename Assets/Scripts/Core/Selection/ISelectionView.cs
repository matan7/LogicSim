namespace Core.Selecting
{
    /// <summary>
    /// A view that shows whether its element is selected. The selection itself holds ids (<see cref="SelectionItem"/>);
    /// the circuit view tells the views of those ids.
    /// </summary>
    public interface ISelectionView
    {
        void SetSelected(bool selected);
    }
}
