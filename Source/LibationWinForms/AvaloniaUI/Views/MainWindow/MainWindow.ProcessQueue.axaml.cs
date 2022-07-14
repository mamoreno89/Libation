﻿using DataLayer;
using Dinah.Core;
using LibationFileManager;
using System;
using System.Linq;

namespace LibationWinForms.AvaloniaUI.Views
{
	//DONE
	public partial class MainWindow
	{
		private void Configure_ProcessQueue()
		{
			var collapseState = !Configuration.Instance.GetNonString<bool>(nameof(splitContainer1.IsPaneOpen));
			SetQueueCollapseState(collapseState);
		}

		public void ProductsDisplay_LiberateClicked(object sender, LibraryBook libraryBook)
		{
			try
			{
				if (libraryBook.Book.UserDefinedItem.BookStatus is LiberatedStatus.NotLiberated or LiberatedStatus.PartialDownload)
				{
					Serilog.Log.Logger.Information("Begin single book backup of {libraryBook}", libraryBook);
					SetQueueCollapseState(false);
					processBookQueue1.AddDownloadDecrypt(libraryBook);
				}
				else if (libraryBook.Book.UserDefinedItem.PdfStatus is LiberatedStatus.NotLiberated)
				{
					Serilog.Log.Logger.Information("Begin single pdf backup of {libraryBook}", libraryBook);
					SetQueueCollapseState(false);
					processBookQueue1.AddDownloadPdf(libraryBook);
				}
				else if (libraryBook.Book.Audio_Exists())
				{
					// liberated: open explorer to file
					var filePath = AudibleFileStorage.Audio.GetPath(libraryBook.Book.AudibleProductId);
					if (!Go.To.File(filePath?.ShortPathName))
					{
						var suffix = string.IsNullOrWhiteSpace(filePath) ? "" : $":\r\n{filePath}";
						System.Windows.Forms.MessageBox.Show($"File not found" + suffix);
					}
				}
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, "An error occurred while handling the stop light button click for {libraryBook}", libraryBook);
			}
		}
		private void SetQueueCollapseState(bool collapsed)
		{
			splitContainer1.IsPaneOpen = !collapsed;
			toggleQueueHideBtn.Content = splitContainer1.IsPaneOpen ? "❱❱❱" : "❰❰❰";
		}

		public void ToggleQueueHideBtn_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			SetQueueCollapseState(splitContainer1.IsPaneOpen);
			Configuration.Instance.SetObject(nameof(splitContainer1.IsPaneOpen), splitContainer1.IsPaneOpen);
		}
	}
}
