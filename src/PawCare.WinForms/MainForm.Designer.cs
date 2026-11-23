namespace PawCare.WinForms
{
    partial class MainForm
    {
        private System.Windows.Forms.Button checkButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.ListBox branchList;

        private void InitializeComponent()
        {
            checkButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            branchList = new System.Windows.Forms.ListBox();
            SuspendLayout();

            checkButton.Location = new System.Drawing.Point(20, 20);
            checkButton.Size = new System.Drawing.Size(145, 30);
            checkButton.Text = "Load branches";
            checkButton.Click += CheckApi;

            branchList.AccessibleName = "Registered PawCare branches";
            branchList.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            branchList.Location = new System.Drawing.Point(20, 63);
            branchList.Size = new System.Drawing.Size(420, 112);
            branchList.IntegralHeight = false;

            statusLabel.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right;
            statusLabel.Location = new System.Drawing.Point(20, 187);
            statusLabel.Size = new System.Drawing.Size(420, 28);
            statusLabel.Text = "Load branches to check the API and SQL Server connection.";

            ClientSize = new System.Drawing.Size(460, 228);
            MinimumSize = new System.Drawing.Size(420, 240);
            Controls.Add(checkButton);
            Controls.Add(branchList);
            Controls.Add(statusLabel);
            Text = "PawCare Branch Directory";
            ResumeLayout(false);
        }
    }
}
