using System.Collections.Generic;

namespace Core.Selecting
{
    public class SelectingService 
    {
        public List<ISelectable> SelectionList { get; private set; } = new List<ISelectable>();

        public void DeselectAll()
        {
            for (int i = 0; i < SelectionList.Count; i++)
            {
                SelectionList[i].Deselect();
            }
            SelectionList.Clear();
        }

        public void Select(ISelectable selectable)
        {
            if (SelectionList.Contains(selectable))
                return;
            SelectionList.Add(selectable);
            selectable.Select();
        }

        public void Deselect(ISelectable selectable)
        {
            SelectionList.Remove(selectable);
            selectable.Deselect();
        }
    }
}
