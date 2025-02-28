﻿using DataLayer;
using Dinah.Core.WindowsDesktop.Forms;
using LibationFileManager;
using LibationUiBase.GridView;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibationWinForms.GridView
{
	public delegate void GridEntryClickedEventHandler(IGridEntry liveGridEntry);
	public delegate void LibraryBookEntryClickedEventHandler(ILibraryBookEntry liveGridEntry);
	public delegate void GridEntryRectangleClickedEventHandler(IGridEntry liveGridEntry, Rectangle cellRectangle);
	public delegate void ProductsGridCellContextMenuStripNeededEventHandler(IGridEntry liveGridEntry, ContextMenuStrip ctxMenu);

	public partial class ProductsGrid : UserControl
	{
		/// <summary>Number of visible rows has changed</summary>
		public event EventHandler<int> VisibleCountChanged;
		public event LibraryBookEntryClickedEventHandler LiberateClicked;
		public event GridEntryClickedEventHandler CoverClicked;
		public event LibraryBookEntryClickedEventHandler DetailsClicked;
		public event GridEntryRectangleClickedEventHandler DescriptionClicked;
		public new event EventHandler<ScrollEventArgs> Scroll;
		public event EventHandler RemovableCountChanged;
		public event ProductsGridCellContextMenuStripNeededEventHandler LiberateContextMenuStripNeeded;

		private GridEntryBindingList bindingList;
		internal IEnumerable<LibraryBook> GetVisibleBooks()
			=> bindingList
			.GetFilteredInItems()
			.Select(lbe => lbe.LibraryBook);
		internal IEnumerable<ILibraryBookEntry> GetAllBookEntries()
			=> bindingList.AllItems().BookEntries();

		public ProductsGrid()
		{
			InitializeComponent();
			EnableDoubleBuffering();
			gridEntryDataGridView.Scroll += (_, s) => Scroll?.Invoke(this, s);
			gridEntryDataGridView.CellContextMenuStripNeeded += GridEntryDataGridView_CellContextMenuStripNeeded;
			removeGVColumn.Frozen = false;
		}

		private void GridEntryDataGridView_CellContextMenuStripNeeded(object sender, DataGridViewCellContextMenuStripNeededEventArgs e)
		{
			// header
			if (e.RowIndex < 0)
				return;

			// cover
			else if (e.ColumnIndex == coverGVColumn.Index)
				return;

			e.ContextMenuStrip = new ContextMenuStrip();
			// any non-stop light
			if (e.ColumnIndex != liberateGVColumn.Index)
			{
				e.ContextMenuStrip.Items.Add("Copy", null, (_, __) =>
				{
					try
					{
						var dgv = (DataGridView)sender;
						var text = dgv[e.ColumnIndex, e.RowIndex].FormattedValue.ToString();
						Clipboard.SetDataObject(text, false, 5, 150);
					}
					catch { }
				});				
			}
			else
			{
				var entry = getGridEntry(e.RowIndex);
				var name = gridEntryDataGridView.Columns[e.ColumnIndex].DataPropertyName;
				LiberateContextMenuStripNeeded?.Invoke(entry, e.ContextMenuStrip);
			}
		}

		private void EnableDoubleBuffering()
		{
			var propertyInfo = gridEntryDataGridView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

			propertyInfo.SetValue(gridEntryDataGridView, true, null);
		}

		#region Button controls
		private void DataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
		{
			try
			{
				// handle grid button click: https://stackoverflow.com/a/13687844
				if (e.RowIndex < 0)
					return;

				var entry = getGridEntry(e.RowIndex);
				if (entry is ILibraryBookEntry lbEntry)
				{
					if (e.ColumnIndex == liberateGVColumn.Index)
						LiberateClicked?.Invoke(lbEntry);
					else if (e.ColumnIndex == tagAndDetailsGVColumn.Index)
						DetailsClicked?.Invoke(lbEntry);
					else if (e.ColumnIndex == descriptionGVColumn.Index)
						DescriptionClicked?.Invoke(lbEntry, gridEntryDataGridView.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false));
					else if (e.ColumnIndex == coverGVColumn.Index)
						CoverClicked?.Invoke(lbEntry);
				}
				else if (entry is ISeriesEntry sEntry)
				{
					if (e.ColumnIndex == liberateGVColumn.Index)
					{
						if (sEntry.Liberate.Expanded)
							bindingList.CollapseItem(sEntry);
						else
							bindingList.ExpandItem(sEntry);

						VisibleCountChanged?.Invoke(this, bindingList.GetFilteredInItems().Count());
					}
					else if (e.ColumnIndex == descriptionGVColumn.Index)
						DescriptionClicked?.Invoke(sEntry, gridEntryDataGridView.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false));
					else if (e.ColumnIndex == coverGVColumn.Index)
						CoverClicked?.Invoke(sEntry);
				}

				if (e.ColumnIndex == removeGVColumn.Index)
				{
					gridEntryDataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
					RemovableCountChanged?.Invoke(this, EventArgs.Empty);
				}
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, $"An error was encountered while processing a user click in the {nameof(ProductsGrid)}");
			}
		}

		private IGridEntry getGridEntry(int rowIndex) => gridEntryDataGridView.GetBoundItem<IGridEntry>(rowIndex);

		#endregion

		#region UI display functions

		internal bool RemoveColumnVisible
		{
			get => removeGVColumn.Visible;
			set
			{
				if (value)
				{
					foreach (var book in bindingList.AllItems())
						book.Remove = false;
				}

				removeGVColumn.DisplayIndex = 0;
				removeGVColumn.Frozen = value;
				removeGVColumn.Visible = value;
			}
		}

		internal void BindToGrid(List<LibraryBook> dbBooks)
		{
			var geList = dbBooks
				.Where(lb => lb.Book.IsProduct())
				.Select(b => new LibraryBookEntry<WinFormsEntryStatus>(b))
				.ToList<IGridEntry>();

			var episodes = dbBooks.Where(lb => lb.Book.IsEpisodeChild());

			var seriesBooks = dbBooks.Where(lb => lb.Book.IsEpisodeParent()).ToList();

			foreach (var parent in seriesBooks)
			{
				var seriesEpisodes = episodes.FindChildren(parent);

				if (!seriesEpisodes.Any()) continue;

				var seriesEntry = new SeriesEntry<WinFormsEntryStatus>(parent, seriesEpisodes);

				geList.Add(seriesEntry);
				geList.AddRange(seriesEntry.Children);
			}

			bindingList = new GridEntryBindingList(geList);
			bindingList.CollapseAll();
			syncBindingSource.DataSource = bindingList;
			VisibleCountChanged?.Invoke(this, bindingList.GetFilteredInItems().Count());
		}

		internal void UpdateGrid(List<LibraryBook> dbBooks)
		{
			//First row that is in view in the DataGridView
			var topRow = gridEntryDataGridView.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Displayed)?.Index ?? 0;

			#region Add new or update existing grid entries

			//Remove filter prior to adding/updating boooks
			string existingFilter = syncBindingSource.Filter;
			Filter(null);

			//Add absent entries to grid, or update existing entry

			var allEntries = bindingList.AllItems().BookEntries();
			var seriesEntries = bindingList.AllItems().SeriesEntries().ToList();
			var parentedEpisodes = dbBooks.ParentedEpisodes().ToHashSet();

			bindingList.RaiseListChangedEvents = false;
			foreach (var libraryBook in dbBooks.OrderBy(e => e.DateAdded))
			{
				var existingEntry = allEntries.FindByAsin(libraryBook.Book.AudibleProductId);

				if (libraryBook.Book.IsProduct())
				{
					AddOrUpdateBook(libraryBook, existingEntry);
					continue;
				}
				if (parentedEpisodes.Contains(libraryBook))
				{
					//Only try to add or update is this LibraryBook is a know child of a parent
					AddOrUpdateEpisode(libraryBook, existingEntry, seriesEntries, dbBooks);
				}
			}
			bindingList.RaiseListChangedEvents = true;

			//Re-apply filter after adding new/updating existing books to capture any changes
			//The Filter call also ensures that the binding list is reset so the DataGridView
			//is made aware of all changes that were made while RaiseListChangedEvents was false
			Filter(existingFilter);

			#endregion

			// remove deleted from grid.
			// note: actual deletion from db must still occur via the RemoveBook feature. deleting from audible will not trigger this
			var removedBooks =
				bindingList
				.AllItems()
				.BookEntries()
				.ExceptBy(dbBooks.Select(lb => lb.Book.AudibleProductId), ge => ge.AudibleProductId);

			RemoveBooks(removedBooks);

			gridEntryDataGridView.FirstDisplayedScrollingRowIndex = topRow;
		}

		public void RemoveBooks(IEnumerable<ILibraryBookEntry> removedBooks)
		{
			//Remove books in series from their parents' Children list
			foreach (var removed in removedBooks.Where(b => b.Liberate.IsEpisode))
				removed.Parent.RemoveChild(removed);

			//Remove series that have no children
			var removedSeries =
				bindingList
				.AllItems()
				.EmptySeries();

			foreach (var removed in removedBooks.Cast<IGridEntry>().Concat(removedSeries))
				//no need to re-filter for removed books
				bindingList.Remove(removed);

			VisibleCountChanged?.Invoke(this, bindingList.GetFilteredInItems().Count());
		}

		private void AddOrUpdateBook(LibraryBook book, ILibraryBookEntry existingBookEntry)
		{
			if (existingBookEntry is null)
				// Add the new product to top
				bindingList.Insert(0, new LibraryBookEntry<WinFormsEntryStatus>(book));
			else
				// update existing
				existingBookEntry.UpdateLibraryBook(book);
		}

		private void AddOrUpdateEpisode(LibraryBook episodeBook, ILibraryBookEntry existingEpisodeEntry, List<ISeriesEntry> seriesEntries, IEnumerable<LibraryBook> dbBooks)
		{
			if (existingEpisodeEntry is null)
			{
				ILibraryBookEntry episodeEntry;

				var seriesEntry = seriesEntries.FindSeriesParent(episodeBook);

				if (seriesEntry is null)
				{
					//Series doesn't exist yet, so create and add it
					var seriesBook = dbBooks.FindSeriesParent(episodeBook);

					if (seriesBook is null)
					{
						//This is only possible if the user's db  has some malformed
						//entries from earlier Libation releases that could not be
						//automatically fixed. Log, but don't throw.
						Serilog.Log.Logger.Error("Episode={0}, Episode Series: {1}", episodeBook, episodeBook.Book.SeriesNames());
						return;
					}

					seriesEntry = new SeriesEntry<WinFormsEntryStatus>(seriesBook, episodeBook);
					seriesEntries.Add(seriesEntry);

					episodeEntry = seriesEntry.Children[0];
					seriesEntry.Liberate.Expanded = true;
					bindingList.Insert(0, seriesEntry);
				}
				else
				{
					//Series exists. Create and add episode child then update the SeriesEntry
					episodeEntry = new LibraryBookEntry<WinFormsEntryStatus>(episodeBook, seriesEntry);
					seriesEntry.Children.Add(episodeEntry);
					seriesEntry.Children.Sort((c1,c2) => c1.SeriesIndex.CompareTo(c2.SeriesIndex));
					var seriesBook = dbBooks.Single(lb => lb.Book.AudibleProductId == seriesEntry.LibraryBook.Book.AudibleProductId);
					seriesEntry.UpdateLibraryBook(seriesBook);
				}

				//Add episode to the grid beneath the parent
				int seriesIndex = bindingList.IndexOf(seriesEntry);
				int episodeIndex = seriesEntry.Children.IndexOf(episodeEntry);
				bindingList.Insert(seriesIndex + 1 + episodeIndex, episodeEntry);

				if (seriesEntry.Liberate.Expanded)
					bindingList.ExpandItem(seriesEntry);
				else
					bindingList.CollapseItem(seriesEntry);
			}
			else
				existingEpisodeEntry.UpdateLibraryBook(episodeBook);
		}

		#endregion

		#region Filter

		public void Filter(string searchString)
		{
			int visibleCount = bindingList.Count;

			if (string.IsNullOrEmpty(searchString))
				syncBindingSource.RemoveFilter();
			else
				syncBindingSource.Filter = searchString;

			if (visibleCount != bindingList.Count)
				VisibleCountChanged?.Invoke(this, bindingList.GetFilteredInItems().Count());
		}

		#endregion

		#region Column Customizations

		private void ProductsGrid_Load(object sender, EventArgs e)
		{
			//https://stackoverflow.com/a/4498512/3335599
			if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime) return;

			gridEntryDataGridView.ColumnWidthChanged += gridEntryDataGridView_ColumnWidthChanged;
			gridEntryDataGridView.ColumnDisplayIndexChanged += gridEntryDataGridView_ColumnDisplayIndexChanged;

			showHideColumnsContextMenuStrip.Items.Add(new ToolStripLabel("Show / Hide Columns"));
			showHideColumnsContextMenuStrip.Items.Add(new ToolStripSeparator());

			//Restore Grid Display Settings
			var config = Configuration.Instance;
			var gridColumnsVisibilities = config.GridColumnsVisibilities;
			var gridColumnsWidths = config.GridColumnsWidths;
			var displayIndices = config.GridColumnsDisplayIndices;

			var cmsKiller = new ContextMenuStrip();

			foreach (DataGridViewColumn column in gridEntryDataGridView.Columns)
			{
				var itemName = column.DataPropertyName;
				var visible = gridColumnsVisibilities.GetValueOrDefault(itemName, true);

				var menuItem = new ToolStripMenuItem()
				{
					Text = column.HeaderText,
					Checked = visible,
					Tag = itemName
				};
				menuItem.Click += HideMenuItem_Click;
				showHideColumnsContextMenuStrip.Items.Add(menuItem);

				column.Width = gridColumnsWidths.GetValueOrDefault(itemName, column.Width);
				column.MinimumWidth = 10;
				column.HeaderCell.ContextMenuStrip = showHideColumnsContextMenuStrip;
				column.Visible = visible;

				//Setting a default ContextMenuStrip will allow the columns to handle the
				//Show() event so it is not passed up to the _dataGridView.ContextMenuStrip.
				//This allows the ContextMenuStrip to be shown if right-clicking in the gray
				//background of _dataGridView but not shown if right-clicking inside cells.
				column.ContextMenuStrip = cmsKiller;
			}

			//We must set DisplayIndex properties in ascending order
			foreach (var itemName in displayIndices.OrderBy(i => i.Value).Select(i => i.Key))
			{
				var column = gridEntryDataGridView.Columns
					.Cast<DataGridViewColumn>()
					.SingleOrDefault(c => c.DataPropertyName == itemName);

				if (column is null) continue;

				column.DisplayIndex = displayIndices.GetValueOrDefault(itemName, column.Index);
			}

			//Remove column is always first;
			removeGVColumn.DisplayIndex = 0;
			removeGVColumn.Visible = false;
			removeGVColumn.ValueType = typeof(bool?);
			removeGVColumn.FalseValue = false;
			removeGVColumn.TrueValue = true;
			removeGVColumn.IndeterminateValue = null;
		}

		private void HideMenuItem_Click(object sender, EventArgs e)
		{
			var menuItem = sender as ToolStripMenuItem;
			var propertyName = menuItem.Tag as string;

			var column = gridEntryDataGridView.Columns
				.Cast<DataGridViewColumn>()
				.FirstOrDefault(c => c.DataPropertyName == propertyName);

			if (column != null)
			{
				var visible = menuItem.Checked;
				menuItem.Checked = !visible;
				column.Visible = !visible;

				var config = Configuration.Instance;

				var dictionary = config.GridColumnsVisibilities;
				dictionary[propertyName] = column.Visible;
				config.GridColumnsVisibilities = dictionary;
			}
		}

		private void gridEntryDataGridView_ColumnDisplayIndexChanged(object sender, DataGridViewColumnEventArgs e)
		{
			var config = Configuration.Instance;

			var dictionary = config.GridColumnsDisplayIndices;
			dictionary[e.Column.DataPropertyName] = e.Column.DisplayIndex;
			config.GridColumnsDisplayIndices = dictionary;
		}

		private void gridEntryDataGridView_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
		{
			if (e.ColumnIndex == descriptionGVColumn.Index)
				e.ToolTipText = "Click to see full description";
			else if (e.ColumnIndex == coverGVColumn.Index)
				e.ToolTipText = "Click to see full size";
		}

		private void gridEntryDataGridView_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
		{
			var config = Configuration.Instance;

			var dictionary = config.GridColumnsWidths;
			dictionary[e.Column.DataPropertyName] = e.Column.Width;
		}

		#endregion
	}
}
