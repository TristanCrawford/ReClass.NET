﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ReClassNET.Extensions;
using ReClassNET.Memory;
using ReClassNET.Nodes;
using ReClassNET.Util;

namespace ReClassNET.UI
{
	public partial class MemoryViewControl : ScrollableCustomControl
	{
		/// <summary>
		/// Contains informations about a selected node.
		/// </summary>
		public class SelectedNodeInfo
		{
			/// <summary>
			/// The selected node.
			/// </summary>
			public BaseNode Node { get; }
			
			/// <summary>
			/// The memory this node uses.
			/// </summary>
			public MemoryBuffer Memory { get; }

			/// <summary>
			/// The address of the node in the remote process.
			/// </summary>
			public IntPtr Address { get; }

			public int Level { get; }

			public SelectedNodeInfo(BaseNode node, MemoryBuffer memory, IntPtr address, int level)
			{
				Contract.Requires(node != null);
				Contract.Requires(memory != null);

				Node = node;
				Memory = memory;
				Address = address;
				Level = level;
			}
		}

		private ReClassNetProject project;

		private ClassNode classNode;

		private readonly List<HotSpot> hotSpots = new List<HotSpot>();
		private readonly List<HotSpot> selectedNodes = new List<HotSpot>();

		private HotSpot selectionCaret;
		private HotSpot selectionAnchor;

		private readonly FontEx font;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ReClassNetProject Project
		{
			get => project;
			set
			{
				Contract.Requires(value != null);

				project = value;
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ClassNode ClassNode
		{
			get => classNode;
			set
			{
				editBox.Visible = false;

				ClearSelection();

				OnSelectionChanged();

				classNode = value;
				
				VerticalScroll.Value = VerticalScroll.Minimum;
				if (classNode != null && Memory?.Process != null)
				{
					classNode.UpdateAddress(Memory.Process);
				}
				
				Invalidate();
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public MemoryBuffer Memory { get; set; }

		public event EventHandler SelectionChanged;
		public event NodeClickEventHandler ChangeClassTypeClick;
		public event NodeClickEventHandler ChangeWrappedTypeClick;

		private readonly MemoryPreviewPopUp memoryPreviewPopUp;

		public MemoryViewControl()
		{
			InitializeComponent();

			if (Program.DesignMode)
			{
				return;
			}

			font = Program.MonoSpaceFont;

			editBox.Font = font;

			memoryPreviewPopUp = new MemoryPreviewPopUp(font);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			VerticalScroll.Enabled = true;
			VerticalScroll.Visible = true;
			VerticalScroll.SmallChange = 10;
			HorizontalScroll.Enabled = true;
			HorizontalScroll.Visible = true;
			HorizontalScroll.SmallChange = 100;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			if (DesignMode)
			{
				e.Graphics.FillRectangle(Brushes.White, ClientRectangle);

				return;
			}

			hotSpots.Clear();

			using (var brush = new SolidBrush(Program.Settings.BackgroundColor))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}

			if (ClassNode == null)
			{
				return;
			}

			if (Memory.Process != null)
			{
				ClassNode.UpdateAddress(Memory.Process);
			}

			if (memoryPreviewPopUp.Visible)
			{
				memoryPreviewPopUp.UpdateMemory();
			}

			Memory.Size = ClassNode.MemorySize;
			Memory.Update(ClassNode.Offset);

			var view = new ViewInfo
			{
				Settings = Program.Settings,
				Context = e.Graphics,
				Font = font,
				Memory = Memory,
				CurrentTime = DateTime.UtcNow,
				ClientArea = ClientRectangle,
				HotSpots = hotSpots,
				Address = ClassNode.Offset,
				Level = 0,
				MultipleNodesSelected = selectedNodes.Count > 1
			};

			try
			{
				var drawnSize = ClassNode.Draw(
					view,
					-HorizontalScroll.Value,
					-VerticalScroll.Value * font.Height
				);
				drawnSize.Width += 50;

				/*foreach (var spot in hotSpots.Where(h => h.Type == HotSpotType.Select))
				{
					e.Graphics.DrawRectangle(new Pen(new SolidBrush(Color.FromArgb(150, 255, 0, 0)), 1), spot.Rect);
				}*/

				if (drawnSize.Height > ClientSize.Height)
				{
					VerticalScroll.Enabled = true;

					VerticalScroll.LargeChange = ClientSize.Height / font.Height;
					VerticalScroll.Maximum = (drawnSize.Height - ClientSize.Height) / font.Height + VerticalScroll.LargeChange;
				}
				else
				{
					VerticalScroll.Enabled = false;

					VerticalScroll.Value = VerticalScroll.Minimum;
				}

				if (drawnSize.Width > ClientSize.Width)
				{
					HorizontalScroll.Enabled = true;

					HorizontalScroll.LargeChange = ClientSize.Width;
					HorizontalScroll.Maximum = drawnSize.Width - ClientSize.Width + HorizontalScroll.LargeChange;
				}
				else
				{
					HorizontalScroll.Enabled = false;

					HorizontalScroll.Value = HorizontalScroll.Minimum;
				}
			}
			catch (Exception)
			{
				Debug.Assert(false);
			}
		}

		private void OnSelectionChanged()
		{
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		#region Process Input

		protected override void OnMouseClick(MouseEventArgs e)
		{
			Contract.Requires(e != null);

			var invalidate = false;

			foreach (var hotSpot in hotSpots)
			{
				if (hotSpot.Rect.Contains(e.Location))
				{
					try
					{
						var hitObject = hotSpot.Node;

						if (hotSpot.Type == HotSpotType.OpenClose)
						{
							hitObject.ToggleLevelOpen(hotSpot.Level);

							invalidate = true;

							break;
						}
						if (hotSpot.Type == HotSpotType.Click)
						{
							hitObject.Update(hotSpot);

							invalidate = true;

							break;
						}
						if (hotSpot.Type == HotSpotType.Select)
						{
							if (e.Button == MouseButtons.Left)
							{
								if (ModifierKeys == Keys.None)
								{
									ClearSelection();

									hitObject.IsSelected = true;

									selectedNodes.Add(hotSpot);

									OnSelectionChanged();

									selectionAnchor = selectionCaret = hotSpot;
								}
								else if (ModifierKeys == Keys.Control)
								{
									hitObject.IsSelected = !hitObject.IsSelected;

									if (hitObject.IsSelected)
									{
										selectedNodes.Add(hotSpot);
									}
									else
									{
										selectedNodes.Remove(selectedNodes.FirstOrDefault(c => c.Node == hitObject));
									}

									OnSelectionChanged();
								}
								else if (ModifierKeys == Keys.Shift)
								{
									if (selectedNodes.Count > 0)
									{
										var selectedNode = selectedNodes[0].Node;
										if (hitObject.GetParentContainer() != null && selectedNode.GetParentContainer() != hitObject.GetParentContainer())
										{
											continue;
										}

										if (hotSpot.Node is BaseContainerNode)
										{
											continue;
										}

										var first = Utils.Min(selectedNodes[0], hotSpot, h => h.Node.Offset.ToInt32());
										var last = first == hotSpot ? selectedNodes[0] : hotSpot;

										ClearSelection();

										var containerNode = selectedNode.GetParentContainer();
										foreach (var spot in containerNode.Nodes
											.SkipWhile(n => n != first.Node)
											.TakeUntil(n => n == last.Node)
											.Select(n => new HotSpot
											{
												Address = containerNode.Offset.Add(n.Offset),
												Node = n,
												Memory = first.Memory,
												Level = first.Level
											}))
										{
											spot.Node.IsSelected = true;
											selectedNodes.Add(spot);
										}

										OnSelectionChanged();

										selectionAnchor = first;
										selectionCaret = last;
									}
								}
							}
							else if (e.Button == MouseButtons.Right)
							{
								// If there is only one selected node, select the node the user clicked at.
								if (selectedNodes.Count <= 1)
								{
									ClearSelection();

									hitObject.IsSelected = true;

									selectedNodes.Add(hotSpot);

									OnSelectionChanged();

									selectionAnchor = selectionCaret = hotSpot;
								}

								ShowNodeContextMenu(e.Location);
							}

							invalidate = true;
						}
						else if (hotSpot.Type == HotSpotType.Drop)
						{
							ShowNodeContextMenu(e.Location);

							break;
						}
						else if (hotSpot.Type == HotSpotType.Delete)
						{
							hotSpot.Node.GetParentContainer().RemoveNode(hotSpot.Node);

							invalidate = true;

							break;
						}
						else if (hotSpot.Type == HotSpotType.ChangeClassType)
						{
							var handler = ChangeClassTypeClick;
							handler?.Invoke(this, new NodeClickEventArgs(hitObject, e.Button, e.Location));

							break;
						}
						else if (hotSpot.Type == HotSpotType.ChangeWrappedType)
						{
							var handler = ChangeWrappedTypeClick;
							handler?.Invoke(this, new NodeClickEventArgs(hitObject, e.Button, e.Location));

							break;
						}
					}
					catch (Exception ex)
					{
						Program.Logger.Log(ex);
					}
				}
			}

			editBox.Visible = false;

			if (invalidate)
			{
				Invalidate();
			}

			base.OnMouseClick(e);
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			Contract.Requires(e != null);

			editBox.Visible = false;

			var invalidate = false;

			// Order the hotspots: 1. DoubleClick 2. Click 3. Edit 4. Select
			var spots = hotSpots.Where(h => h.Type == HotSpotType.DoubleClick)
				.Concat(hotSpots.Where(h => h.Type == HotSpotType.Click))
				.Concat(hotSpots.Where(h => h.Type == HotSpotType.Edit))
				.Concat(hotSpots.Where(h => h.Type == HotSpotType.Select));

			foreach (var hotSpot in spots)
			{
				if (hotSpot.Rect.Contains(e.Location))
				{
					if (hotSpot.Type == HotSpotType.DoubleClick || hotSpot.Type == HotSpotType.Click)
					{
						hotSpot.Node.Update(hotSpot);

						invalidate = true;

						break;
					}
					if (hotSpot.Type == HotSpotType.Edit)
					{
						editBox.BackColor = Program.Settings.SelectedColor;
						editBox.HotSpot = hotSpot;
						editBox.Visible = true;

						editBox.ReadOnly = hotSpot.Id == HotSpot.ReadOnlyId;

						break;
					}
					if (hotSpot.Type == HotSpotType.Select)
					{
						hotSpot.Node.ToggleLevelOpen(hotSpot.Level);

						invalidate = true;

						break;
					}
				}
			}

			if (invalidate)
			{
				Invalidate();
			}

			base.OnMouseDoubleClick(e);
		}

		private Point toolTipPosition;
		protected override void OnMouseHover(EventArgs e)
		{
			Contract.Requires(e != null);

			base.OnMouseHover(e);

			if (selectedNodes.Count > 1)
			{
				var memorySize = selectedNodes.Sum(h => h.Node.MemorySize);
				nodeInfoToolTip.Show($"{selectedNodes.Count} Nodes selected, {memorySize} bytes", this, toolTipPosition.OffsetEx(16, 16));
			}
			else
			{
				foreach (var spot in hotSpots.Where(h => h.Type == HotSpotType.Select))
				{
					if (spot.Rect.Contains(toolTipPosition))
					{
						if (spot.Node.UseMemoryPreviewToolTip(spot, spot.Memory, out var previewAddress))
						{
							memoryPreviewPopUp.InitializeMemory(spot.Memory.Process, previewAddress);

							memoryPreviewPopUp.Show(this, toolTipPosition.OffsetEx(16, 16));
						}
						else
						{
							var text = spot.Node.GetToolTipText(spot, spot.Memory);
							if (!string.IsNullOrEmpty(text))
							{
								nodeInfoToolTip.Show(text, this, toolTipPosition.OffsetEx(16, 16));
							}
						}

						return;
					}
				}
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			Contract.Requires(e != null);

			base.OnMouseMove(e);

			if (e.Location != toolTipPosition)
			{
				toolTipPosition = e.Location;

				nodeInfoToolTip.Hide(this);

				if (memoryPreviewPopUp.Visible)
				{
					memoryPreviewPopUp.Close();

					Invalidate();
				}

				ResetMouseEventArgs();
			}
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (memoryPreviewPopUp.Visible)
			{
				memoryPreviewPopUp.HandleMouseWheelEvent(e);
			}
			else
			{
				base.OnMouseWheel(e);
			}
		}

		protected override void OnScroll(ScrollEventArgs e)
		{
			Contract.Requires(e != null);

			base.OnScroll(e);

			editBox.Visible = false;
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (editBox.Visible == false) // Only process keys if the edit field is not visible.
			{
				var key = keyData & Keys.KeyCode;
				var modifier = keyData & Keys.Modifiers;

				if (selectedNodes.Count > 0)
				{
					if (key == Keys.Menu)
					{
						ShowNodeContextMenu(new Point(10, 10));

						return true;
					}
					if ((key == Keys.Down || key == Keys.Up) && selectionCaret != null && selectionAnchor != null)
					{
						HotSpot toSelect;
						bool isAtEnd;

						var query = hotSpots
							.Where(h => h.Type == HotSpotType.Select)
							.Where(h => h.Node.GetParentContainer() == selectionCaret.Node.GetParentContainer());

						if (key == Keys.Down)
						{
							var temp = query
								.SkipUntil(h => h.Node == selectionCaret.Node)
								.ToList();

							toSelect = temp.FirstOrDefault();
							isAtEnd = toSelect != null && toSelect == temp.LastOrDefault();
						}
						else
						{
							var temp = query
								.TakeWhile(h => h.Node != selectionCaret.Node)
								.ToList();

							toSelect = temp.LastOrDefault();
							isAtEnd = toSelect != null && toSelect == temp.FirstOrDefault();
						}

						if (toSelect != null && !(toSelect.Node is ClassNode))
						{
							if (modifier != Keys.Shift)
							{
								selectionAnchor = selectionCaret = toSelect;
							}
							else
							{
								selectionCaret = toSelect;
							}

							var first = Utils.Min(selectionAnchor, selectionCaret, h => h.Node.Offset.ToInt32());
							var last = first == selectionAnchor ? selectionCaret : selectionAnchor;

							selectedNodes.ForEach(h => h.Node.ClearSelection());
							selectedNodes.Clear();

							var containerNode = toSelect.Node.GetParentContainer();
							foreach (var spot in containerNode.Nodes
								.SkipWhile(n => n != first.Node)
								.TakeUntil(n => n == last.Node)
								.Select(n => new HotSpot
								{
									Address = containerNode.Offset.Add(n.Offset),
									Node = n,
									Memory = toSelect.Memory,
									Level = toSelect.Level
								}))
							{
								spot.Node.IsSelected = true;
								selectedNodes.Add(spot);
							}

							OnSelectionChanged();

							if (isAtEnd)
							{
								DoScroll(ScrollOrientation.VerticalScroll, key == Keys.Down ? 1 : - 1);
							}

							Invalidate();

							return true;
						}
					}
					else if (key == Keys.Left || key == Keys.Right)
					{
						if (selectedNodes.Count == 1)
						{
							var selected = selectedNodes[0];

							selected.Node.SetLevelOpen(selected.Level, key == Keys.Right);
						}
					}
				}
				else if (key == Keys.Down || key == Keys.Up)
				{
					// If no node is selected, try to select the first one.
					var selection = hotSpots
						.Where(h => h.Type == HotSpotType.Select)
						.WhereNot(h => h.Node is ClassNode)
						.FirstOrDefault();
					if (selection != null)
					{
						selectionAnchor = selectionCaret = selection;

						selection.Node.IsSelected = true;

						selectedNodes.Add(selection);

						OnSelectionChanged();

						return true;
					}
				}
			}

			return base.ProcessCmdKey(ref msg, keyData);
		}

		#endregion

		#region Event Handler

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);

			Invalidate();
		}

		private void repaintTimer_Tick(object sender, EventArgs e)
		{
			if (DesignMode)
			{
				return;
			}

			Invalidate(false);
		}

		private void editBox_Committed(object sender, EventArgs e)
		{
			var hotspotTextBox = sender as HotSpotTextBox;

			var hotSpot = hotspotTextBox?.HotSpot;
			if (hotSpot != null)
			{
				try
				{
					hotSpot.Node.Update(hotSpot);
				}
				catch (Exception ex)
				{
					Program.Logger.Log(ex);
				}

				Invalidate();
			}
		}

		#endregion

		/// <summary>
		/// Gets informations about all selected nodes.
		/// </summary>
		/// <returns>A list with informations about all selected nodes.</returns>
		public IReadOnlyList<SelectedNodeInfo> GetSelectedNodes()
		{
			return selectedNodes
				.Select(h => new SelectedNodeInfo(h.Node, h.Memory, h.Address, h.Level))
				.ToList();
		}

		/// <summary>
		/// Selects the given nodes.
		/// </summary>
		/// <param name="nodes"></param>
		public void SetSelectedNodes(IEnumerable<SelectedNodeInfo> nodes)
		{
			selectedNodes.ForEach(h => h.Node.ClearSelection());

			selectedNodes.Clear();

			selectedNodes.AddRange(nodes.Select(i => new HotSpot { Type = HotSpotType.Select, Node = i.Node, Memory = i.Memory, Address = i.Address, Level = i.Level }));
			selectedNodes.ForEach(h => h.Node.IsSelected = true);

			OnSelectionChanged();
		}

		/// <summary>
		/// Shows the context menu at the given point.
		/// </summary>
		/// <param name="location">The location where the context menu should be shown.</param>
		private void ShowNodeContextMenu(Point location)
		{
			ContextMenuStrip?.Show(this, location);
		}

		/// <summary>
		/// Resets the selection state of all selected nodes.
		/// </summary>
		public void ClearSelection()
		{
			selectionAnchor = selectionCaret = null;

			selectedNodes.ForEach(h => h.Node.ClearSelection());

			selectedNodes.Clear();

			OnSelectionChanged();

			//Invalidate();
		}
	}
}
