﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Globalization;
using UserInterface.Commands;
using System.Runtime.Serialization;
using UserInterface.Views;

namespace UserInterface.Views
{

    /// <summary>
    /// An ExplorerView is a "Windows Explorer" like control that displays a virtual tree control on the left
    /// and a user interface on the right allowing the user to modify properties of whatever they
    /// click on in the tree control. 
    /// </summary>
    /// <remarks>
    /// This "view" class follows the Humble Dialog approach were
    /// no business logic is embedded in this class. This object is told what to do by a 
    /// presenter object which is responsible for populating all controls. The theory is that this
    /// class can be reused for differents types of data.
    /// 
    /// When populating nodes, it is given a list of NodeDescription objects that describes what the
    /// nodes look like.
    /// 
    /// NB: All node paths are compatible with Model node paths and includes the root node name:
    ///     If tree is:
    ///     Simulations
    ///         |
    ///         +-- Test
    ///               |
    ///               +--- Clock
    /// e.g.  .Simulations.Test.Clock
    /// </remarks>
    public partial class ExplorerView : UserControl, IExplorerView
    {
        private string PreviouslySelectedNodePath;
        private string SourcePathOfItemBeingDragged;
        private string NodePathBeforeRename;

        /// <summary>
        /// ExplorerView will invoke this event when it wants the presenter to populate 
        /// direct children of the specified node.
        /// </summary>
        public event EventHandler<NodeDescriptionArgs> PopulateChildNodes;

        /// <summary>
        /// This event will be invoked when the user selects a node.
        /// </summary>
        public event EventHandler<NodeSelectedArgs> NodeSelectedByUser;

        /// <summary>
        /// This event will be invoked when a node is selected not by the user
        /// but by an Undo command.
        /// </summary>
        public event EventHandler<NodeSelectedArgs> NodeSelected;

        /// <summary>
        /// ExplorerView will invoke this event when it wants the presenter to populate 
        /// the main menu with items.
        /// </summary>
        public event EventHandler<MenuDescriptionArgs> PopulateMainMenu;

        /// <summary>
        /// ExplorerView will invoke this event when it wants the presenter to populate
        /// the context (popup) menu for the specified node.
        /// </summary>
        public event EventHandler<MenuDescriptionArgs> PopulateContextMenu;

        /// <summary>
        /// Invoked when a drag operation has commenced. Need to create a DragObject.
        /// </summary>
        public event EventHandler<DragStartArgs> DragStart;

        /// <summary>
        /// Invoked when the view wants to know if a drop is allowed on the specified Node.
        /// </summary>
        public new event EventHandler<AllowDropArgs> AllowDrop;

        /// <summary>
        /// Invoked when a drop has occurred.
        /// </summary>
        public event EventHandler<DropArgs> Drop;

        /// <summary>
        /// Invoked then a node is renamed.
        /// </summary>
        public event EventHandler<NodeRenameArgs> Rename;


        /// <summary>
        /// Constructor
        /// </summary>
        public ExplorerView()
        {
            InitializeComponent();
            StatusWindow.Visible = false;
        }

        /// <summary>
        /// Form has loaded.
        /// </summary>
        private void OnLoad(object sender, EventArgs e)
        {
            PopulateMainToolStrip();
            PopulateNodes(null);
            TreeView.Nodes[0].Expand(); //expand the root tree node
        }


        #region Tree node

        /// <summary>
        /// A property providing access to the currently selected node.
        /// </summary>
        public string CurrentNodePath
        {
            get
            {
                if (TreeView.SelectedNode != null)
                    return FullPath(TreeView.SelectedNode);
                else
                    return "";
            }
            set
            {
                // We want the BeforeSelect event to only fire when user clicks on a node
                // in the tree.
                TreeView.AfterSelect -= TreeView_AfterSelect;

                TreeNode NodeToSelect;
                if (value == "")
                    NodeToSelect = null;
                else
                    NodeToSelect = FindNode(value);

                if (TreeView.SelectedNode != NodeToSelect)
                {
                    TreeView.SelectedNode = NodeToSelect;
                    if (NodeSelected != null)
                        NodeSelected.Invoke(this, new NodeSelectedArgs()
                        {
                            OldNodePath = PreviouslySelectedNodePath,
                            NewNodePath = value
                        });
                }

                TreeView.AfterSelect += TreeView_AfterSelect;
            }
        }

        /// <summary>
        /// Configure the specified tree node using the fields in 'Description'
        /// </summary>
        private void ConfigureNode(TreeNode Node, NodeDescriptionArgs.Description Description)
        {
            Node.Text = Description.Name;
            int imageIndex = TreeImageList.Images.IndexOfKey(Description.ResourceNameForImage);
            if (imageIndex == -1)
            {
                Bitmap Icon = Properties.Resources.ResourceManager.GetObject(Description.ResourceNameForImage) as Bitmap;
                if (Icon != null)
                {
                    TreeImageList.Images.Add(Description.ResourceNameForImage, Icon);
                    imageIndex = TreeImageList.Images.Count - 1;
                }
            }
            Node.ImageKey = Description.ResourceNameForImage;
            Node.SelectedImageKey = Description.ResourceNameForImage;
            Node.Nodes.Clear();
            if (Description.HasChildren)
                Node.Nodes.Add("Loading...");
        }

        /// <summary>
        /// Return a full path for the specified node.
        /// </summary>
        private string FullPath(TreeNode Node)
        {
            return "." + Node.FullPath.Replace("\\", ".");
        }

        /// <summary>
        /// Find a specific node with the node path.
        /// NodePath format: .Parent.Child.SubChild
        /// </summary>
        private TreeNode FindNode(string NamePath)
        {
            if (!NamePath.StartsWith(".", StringComparison.CurrentCulture))
                throw new Exception("Invalid name path '" + NamePath + "'");

            NamePath = NamePath.Remove(0, 1); // Remove the leading '.'

            TreeNode Node = null;

            string[] NamePathBits = NamePath.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            foreach (string PathBit in NamePathBits)
            {
                if (Node == null)
                    Node = FindNode(TreeView.Nodes, PathBit);
                else
                    Node = FindNode(Node.Nodes, PathBit);

                if (Node == null)
                    return null;
            }
            return Node;
        }

        /// <summary>
        /// Find a child node with the specified name under the specified ParentNode.
        /// </summary>
        private static TreeNode FindNode(TreeNodeCollection Nodes, string name)
        {
            foreach (TreeNode ChildNode in Nodes)
                if (ChildNode.Text.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    return ChildNode;

            return null;
        }

        #endregion

        #region Right hand panel
        /// <summary>
        /// Add a user control to the right hand panel. If Control is null then right hand panel will be cleared.
        /// </summary>
        public void AddRightHandView(UserControl Control)
        {
            RightHandPanel.Controls.Clear();
            if (Control != null)
            {
                RightHandPanel.Controls.Add(Control);
                Control.Dock = DockStyle.Fill;
                //Control.BringToFront();

                // In MONO OSX, if the right hand panel isn't given the focus then when the user clicks on 
                // a GridView in the right hand window (e.g. clocks gridview) and starts using the cursor 
                // keys to navigate the grid then the key presses seem to go to the StartPageView in the 
                // other tab in the top level tab control. Then an exception is throw from StartPageView.
                //if (Environment.OSVersion.Platform != PlatformID.Win32NT &&
                //    Environment.OSVersion.Platform != PlatformID.Win32Windows)
                //    RightHandPanel.Focus();  // On Windows this causes a blicking event on the node focus.
            }
        }
        #endregion

        #region Main menu

        /// <summary>
        /// Populate the main menu tool strip.
        /// </summary>
        private void PopulateMainToolStrip()
        {
            if (PopulateMainMenu != null)
            {
                MenuDescriptionArgs Args = new MenuDescriptionArgs();
                PopulateMainMenu(this, Args);

                ToolStrip.Items.Clear();
                foreach (MenuDescriptionArgs.Description Description in Args.Descriptions)
                {
                    Bitmap Icon = Properties.Resources.ResourceManager.GetObject(Description.ResourceNameForImage) as Bitmap;
                    ToolStripItem Button = ToolStrip.Items.Add(Description.Name, Icon, Description.OnClick);
                    Button.TextImageRelation = TextImageRelation.ImageAboveText;
                }
                ToolStrip.Visible = ToolStrip.Items.Count > 0;
            }
        }

        #endregion

        /// <summary>
        /// Change the name of the tab.
        /// </summary>
        public void ChangeTabText(string NewTabName)
        {
            TabPage Page = Parent as TabPage;
            Page.Text = NewTabName;

        }

        /// <summary>
        /// Ask the user if they wish to save the simulation.
        /// </summary>
        /// <returns>Choice for saving the simulation</returns>
        public Int32 AskToSave()
        {
            TabPage Page = Parent as TabPage;
            DialogResult result = MessageBox.Show("Do you want to save changes for " + Page.Text + " ?", "Save changes", MessageBoxButtons.YesNoCancel);
            switch (result)
            {
                case DialogResult.Cancel: return -1;
                case DialogResult.Yes: return 0;
                case DialogResult.No: return 1;
                default: return -1;
            }
        }

        /// <summary>
        /// Add a status message to the explorer window
        /// </summary>
        /// <param name="Message"></param>
        public void ShowMessage(string Message, Models.DataStore.ErrorLevel errorLevel)
        {
            if (!Message.EndsWith("\n"))
                Message = Message + "\n";
            StatusWindow.Visible = Message != null;
            StatusWindow.Select(0, 0);

            if (errorLevel == Models.DataStore.ErrorLevel.Error)
                StatusWindow.SelectionColor = Color.Red;
            else if (errorLevel == Models.DataStore.ErrorLevel.Warning)
                StatusWindow.SelectionColor = Color.Brown;
            else
                StatusWindow.SelectionColor = Color.Blue;

            StatusWindow.SelectedText = Message;
            //StatusWindow.Select(0, Message.Length);

            Application.DoEvents();
        }

        /// <summary>
        /// A helper function that asks user for a SaveAs name and returns their new choice.
        /// </summary>
        /// <returns>Returns the new file name or null if action cancelled by user.</returns>
        public string SaveAs(string OldFilename)
        {
            SaveFileDialog.FileName = OldFilename;
            if (SaveFileDialog.ShowDialog() == DialogResult.OK)
                return SaveFileDialog.FileName;
            else
                return null;
        }

        /// <summary>
        /// Invalidate (redraw) the specified node and its direct child nodes.
        /// </summary>
        public void InvalidateNode(string NodePath, NodeDescriptionArgs.Description Description)
        {
            TreeNode Node = FindNode(NodePath);
            ConfigureNode(Node, Description);
            PopulateNodes(Node);
        }

        /// <summary>
        /// Toggle the 2nd right hand side explorer view on/off
        /// </summary>
        public void ToggleSecondExplorerViewVisible()
        {
            MainForm MainForm = Application.OpenForms[0] as MainForm;
            MainForm.ToggleSecondExplorerViewVisible();
        }

        public void DoRename()
        {
            TreeView.SelectedNode.BeginEdit();
        }

        #region Events

        /// <summary>
        /// User is expanding a node. Populate the child nodes if necessary.
        /// </summary>
        private void OnBeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (PopulateChildNodes != null && e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "Loading...")
            {
                PopulateNodes(e.Node);
            }
        }

        /// <summary>
        /// Populate all direct children under the specified Node. If Node = null then
        /// populate all root nodes.
        /// </summary>
        private void PopulateNodes(TreeNode ParentNode)
        {
            NodeDescriptionArgs Args = new NodeDescriptionArgs();
            if (ParentNode != null)
                Args.NodePath = FullPath(ParentNode);
            PopulateChildNodes.Invoke(this, Args);

            TreeNodeCollection Nodes;
            if (ParentNode == null)
                Nodes = TreeView.Nodes;
            else
                Nodes = ParentNode.Nodes;

            // Make sure we have the right number of child nodes.
            // Add extra nodes if necessary
            while (Args.Descriptions.Count > Nodes.Count)
                Nodes.Add(new TreeNode());

            // Remove unwanted nodes if necessary.
            while (Args.Descriptions.Count < Nodes.Count)
            {
                Console.WriteLine("Removing nodes");
                Nodes.RemoveAt(0);
            }

            // Configure each child node.
            for (int i = 0; i < Args.Descriptions.Count; i++)
                ConfigureNode(Nodes[i], Args.Descriptions[i]);
        }

        /// <summary>
        /// A node is about to be selected. Take note of the previous node path.
        /// </summary>
        private void OnTreeViewBeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            PreviouslySelectedNodePath = CurrentNodePath;
        }

        /// <summary>
        /// A node has been selected. Let the presenter know.
        /// </summary>
        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (NodeSelectedByUser != null)
            {
                NodeSelectedByUser.Invoke(this, new NodeSelectedArgs()
                {
                    OldNodePath = PreviouslySelectedNodePath,
                    NewNodePath = FullPath(e.Node)
                });
            }
        }

        /// <summary>
        /// User has moved the splitter.
        /// </summary>
        private void OnSplitterMoved(object sender, SplitterEventArgs e)
        {
            // There is a bug in mono when run on Mac - looks like the split position isn't working.
            // the workaround below gets around that.
            splitter1.SplitterMoved -= OnSplitterMoved;
            splitter1.SplitPosition = this.PointToClient(MousePosition).X;
            splitter1.SplitterMoved += OnSplitterMoved;
        }

        /// <summary>
        /// User has right clicked on a node, opening the context popup menu. Go create that menu
        /// by asking the presenter what to put on the menu.
        /// </summary>
        private void OnPopupMenuOpening(object sender, CancelEventArgs e)
        {
            if (PopulateContextMenu != null)
            {
                MenuDescriptionArgs Args = new MenuDescriptionArgs();
                PopulateContextMenu(this, Args);

                PopupMenu.Items.Clear();
                foreach (MenuDescriptionArgs.Description Description in Args.Descriptions)
                {
                    Bitmap Icon = Properties.Resources.ResourceManager.GetObject(Description.ResourceNameForImage) as Bitmap;
                    ToolStripMenuItem Button = PopupMenu.Items.Add(Description.Name, Icon, Description.OnClick) as ToolStripMenuItem;
                    Button.TextImageRelation = TextImageRelation.ImageAboveText;
                    Button.Checked = Description.Checked;
                }
            }
            e.Cancel = false;
        }


        #endregion

        #region Drag and drop

        // Looks like drag and drop is broken on Mono on Mac. The data being dragged needs to be
        // serializable which is ok but it still doesn work. Gives the error:
        //     System.Runtime.Serialization.SerializationException: Unexpected binary element: 46
        //     at System.Runtime.Serialization.Formatters.Binary.ObjectReader.ReadObject (BinaryElement element, System.IO.BinaryReader reader, System.Int64& objectId, System.Object& value, System.Runtime.Serialization.SerializationInfo& info) [0x00000] in <filename unknown>:0 

        /// <summary>
        /// Node has begun to be dragged.
        /// </summary>
        private void OnNodeDrag(object sender, ItemDragEventArgs e)
        {
            DragStartArgs Args = new DragStartArgs();
            Args.NodePath = FullPath(e.Item as TreeNode);
            if (DragStart != null)
            {
                DragStart(this, Args);
                if (Args.DragObject != null)
                {
                    SourcePathOfItemBeingDragged = Args.NodePath;
                    DoDragDrop(Args.DragObject, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                }
            }
        }

        /// <summary>
        /// Node has been dragged over another node. Allow a drop here?
        /// </summary>
        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;

			// Get the drop location
			TreeNode DestinationNode = TreeView.GetNodeAt(TreeView.PointToClient(new Point(e.X, e.Y)));
			if (DestinationNode != null)
            {
                AllowDropArgs Args = new AllowDropArgs();
                Args.NodePath = FullPath(DestinationNode);

                string[] Formats = e.Data.GetFormats();
                if (Formats.Length > 0)
                {
                    Args.DragObject = e.Data.GetData(Formats[0]) as ISerializable;
                    if (AllowDrop != null)
                    {
                        AllowDrop(this, Args);
                        if (Args.Allow)
                        {
                            string SourceParent = null;
                            if (SourcePathOfItemBeingDragged != null)
                                SourceParent = Utility.String.ParentName(SourcePathOfItemBeingDragged);

                            // Now determine the effect. If the drag originated from a different view 
                            // (e.g. a toolbox or another file) then only copy is supported.
                            if (SourcePathOfItemBeingDragged == null)
                                e.Effect = DragDropEffects.Copy;  // Dragging from a foreign view.
                            else if (SourceParent == Args.NodePath)
                                e.Effect = DragDropEffects.Copy;  // Dragged node's parent is the node we're currently over
                            else if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt)
                                e.Effect = DragDropEffects.Link;
                            else if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                                e.Effect = DragDropEffects.Move;
                            else
                                e.Effect = DragDropEffects.Copy;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Node has been dropped. Send to presenter.
        /// </summary>
        private void OnDragDrop(object sender, DragEventArgs e)
        {
            TreeNode DestinationNode = TreeView.GetNodeAt(TreeView.PointToClient(new Point(e.X, e.Y)));
            if (DestinationNode != null && Drop != null)
            {
                DropArgs Args = new DropArgs();
                Args.NodePath = FullPath(DestinationNode);

                string[] Formats = e.Data.GetFormats();
                if (Formats.Length > 0)
                {
                    Args.DragObject = e.Data.GetData(Formats[0]) as ISerializable;
                    if (e.Effect == DragDropEffects.Copy)
                        Args.Copied = true;
                    else if (e.Effect == DragDropEffects.Move)
                        Args.Moved = true;
                    else
                        Args.Linked = true;
                    Drop(this, Args);

                    // Under MONO / LINUX seem to need to deselect and reselect the node otherwise no
                    // node is selected after the drop and this causes problems later in OnTreeViewBeforeSelect
                    TreeView.SelectedNode = null;
                    TreeView.SelectedNode = DestinationNode;
                }
            }
            SourcePathOfItemBeingDragged = null;
        }
        #endregion


        /// <summary>
        /// User is about to start renaming a node.
        /// </summary>
        private void OnBeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            NodePathBeforeRename = CurrentNodePath;
            e.CancelEdit = false;
        }

        /// <summary>
        /// User has finished renamed a node.
        /// </summary>
        private void OnAfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (Rename != null)
                Rename(this, new NodeRenameArgs()
                {
                    NodePath = NodePathBeforeRename,
                    NewName = e.Label
                }); 
        }

        /// <summary>
        /// Ensure that a right mouse click selects the node also
        /// </summary>
        void TreeViewNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
        	TreeView.SelectedNode = e.Node; 
        }

        private void OnCloseStatusWindowClick(object sender, EventArgs e)
        {
            StatusWindow.Visible = false;
        }

        private void clearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StatusWindow.Clear();
        }

    }
}

