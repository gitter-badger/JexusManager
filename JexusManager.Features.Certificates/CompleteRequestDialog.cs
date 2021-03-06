﻿// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Web.Administration;

namespace JexusManager.Features.Certificates
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Windows.Forms;

    using Microsoft.Web.Management.Client.Win32;
    using Mono.Security.Authenticode;

    public partial class CompleteRequestDialog : DialogForm
    {
        public CompleteRequestDialog(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            InitializeComponent();
            cbStore.SelectedIndex = 0;
            if (Environment.OSVersion.Version.Major < 8)
            {
                // IMPORTANT: WebHosting store is available since Windows 8.
                cbStore.Enabled = false;
            }

            if (!Helper.IsRunningOnMono())
            {
                NativeMethods.TryAddShieldToButton(btnOK);
            }

            var container = new CompositeDisposable();
            FormClosed += (sender, args) => container.Dispose();

            container.Add(
                Observable.FromEventPattern<EventArgs>(txtName, "TextChanged")
                .Subscribe(evt =>
                {
                    btnOK.Enabled = !string.IsNullOrWhiteSpace(txtName.Text)
                        && !string.IsNullOrWhiteSpace(txtPath.Text);
                }));

            container.Add(
                Observable.FromEventPattern<EventArgs>(btnOK, "Click")
                .Subscribe(evt =>
                {
                    if (!File.Exists(txtPath.Text))
                    {
                        ShowMessage(
                            string.Format(
                                "There was an error while performing this operation.{0}{0}Details:{0}{0}Could not find file '{1}'.",
                                Environment.NewLine,
                                txtPath.Text),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1);
                        return;
                    }

                    // TODO: check administrator permission.
                    var x509 = new X509Certificate2(txtPath.Text);

                    var filename = DialogHelper.GetPrivateKeyFile(x509.Subject);
                    if (!File.Exists(filename))
                    {
                        ShowMessage(
                            string.Format(
                                "There was an error while performing this operation.{0}{0}Details:{0}{0}Could not find private key for '{1}'.",
                                Environment.NewLine,
                                txtPath.Text),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1);
                        return;
                    }

                    x509.PrivateKey = PrivateKey.CreateFromFile(filename).RSA;
                    x509.FriendlyName = txtName.Text;
                    var p12File = DialogHelper.GetTempFileName();
                    var p12pwd = "test";
                    var raw = x509.Export(X509ContentType.Pfx, p12pwd);
                    File.WriteAllBytes(p12File, raw);

                    Item = x509;
                    Store = cbStore.SelectedIndex == 0 ? "Personal" : "WebHosting";

                    try
                    {
                        // add certificate
                        using (var process = new Process())
                        {
                            var start = process.StartInfo;
                            start.Verb = "runas";
                            start.FileName = "cmd";
                            start.Arguments = string.Format("/c \"\"{4}\" /f:\"{0}\" /p:{1} /n:\"{2}\" /s:{3}\"",
                                p12File,
                                p12pwd,
                                txtName.Text,
                                cbStore.SelectedIndex == 0 ? "MY" : "WebHosting",
                                Path.Combine(Environment.CurrentDirectory, "certificateinstaller.exe"));
                            start.CreateNoWindow = true;
                            start.WindowStyle = ProcessWindowStyle.Hidden;
                            process.Start();
                            process.WaitForExit();
                            File.Delete(p12File);
                            if (process.ExitCode == 0)
                            {
                                this.DialogResult = DialogResult.OK;
                            }
                            else
                            {
                                MessageBox.Show(process.ExitCode.ToString());
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // elevation is cancelled.
                    }
                }));

            container.Add(
                Observable.FromEventPattern<EventArgs>(btnBrowse, "Click")
                .Subscribe(evt =>
                {
                    var dialog = new OpenFileDialog
                    {
                        FileName = txtPath.Text
                    };
                    if (dialog.ShowDialog() == DialogResult.Cancel)
                    {
                        return;
                    }

                    txtPath.Text = dialog.FileName;
                }));
        }

        public string Store { get; set; }

        public X509Certificate2 Item { get; set; }

        private void SelfCertificateDialogHelpButtonClicked(object sender, CancelEventArgs e)
        {
            Process.Start("http://go.microsoft.com/fwlink/?LinkId=210528");
        }
    }
}
