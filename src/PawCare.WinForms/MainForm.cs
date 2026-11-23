using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace PawCare.WinForms
{
    public partial class MainForm : Form
    {
        private static readonly HttpClient Client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5147/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        public MainForm()
        {
            InitializeComponent();
        }

        private async void CheckApi(object sender, EventArgs e)
        {
            checkButton.Enabled = false;
            statusLabel.Text = "Loading PawCare branches…";
            try
            {
                using (var response = await Client.GetAsync("api/branches"))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        statusLabel.Text = "Could not load branches (HTTP " + (int)response.StatusCode + ").";
                        return;
                    }

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        var serializer = new DataContractJsonSerializer(typeof(List<Branch>));
                        var branches = (List<Branch>)serializer.ReadObject(stream);
                        branchList.Items.Clear();
                        foreach (var branch in branches)
                        {
                            branchList.Items.Add(branch.Code + " — " + branch.Name);
                        }
                        statusLabel.Text = branches.Count == 0
                            ? "Connected to SQL Server. No active branches are registered."
                            : branches.Count + " active branches loaded from SQL Server.";
                    }
                }
            }
            catch (HttpRequestException)
            {
                statusLabel.Text = "Start the PawCare API, then try again.";
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                statusLabel.Text = "The branch request timed out.";
            }
            catch (SerializationException)
            {
                statusLabel.Text = "The API returned an unreadable branch list.";
            }
            finally
            {
                checkButton.Enabled = true;
            }
        }

        [DataContract]
        private sealed class Branch
        {
            [DataMember(Name = "code")]
            public string Code { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }
        }
    }
}
