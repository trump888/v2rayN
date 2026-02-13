using System;
using System.Collections.Generic;
using System.Windows.Forms;
using v2rayN.Handler;
using v2rayN.Mode;
using v2rayN.Resx;

namespace v2rayN.Forms
{
    public partial class AddServerForm : BaseServerForm
    {
        private TextBox txtUpMbps;
        private TextBox txtDownMbps;
        private TextBox txtObfsPassword;
        private ComboBox cmbObfs;
        private TextBox txtCertSha256;
        private Label lblUpMbps, lblDownMbps, lblObfs, lblObfsPassword, lblCertSha256;

        public AddServerForm()
        {
            InitializeComponent();
            AddNewProtocolControls();
        }

        private void AddNewProtocolControls()
        {
            if (eConfigType == EConfigType.Hysteria2 || eConfigType == EConfigType.Mieru || eConfigType == EConfigType.TUIC)
            {
                int yPos = 120;

                lblUpMbps = new Label();
                lblUpMbps.Text = "Up Mbps:";
                lblUpMbps.Location = new System.Drawing.Point(10, yPos);
                lblUpMbps.Size = new System.Drawing.Size(70, 20);
                panVmess.Controls.Add(lblUpMbps);

                txtUpMbps = new TextBox();
                txtUpMbps.Location = new System.Drawing.Point(85, yPos - 3);
                txtUpMbps.Size = new System.Drawing.Size(100, 20);
                txtUpMbps.Text = "100";
                panVmess.Controls.Add(txtUpMbps);

                yPos += 25;
                lblDownMbps = new Label();
                lblDownMbps.Text = "Down Mbps:";
                lblDownMbps.Location = new System.Drawing.Point(10, yPos);
                lblDownMbps.Size = new System.Drawing.Size(70, 20);
                panVmess.Controls.Add(lblDownMbps);

                txtDownMbps = new TextBox();
                txtDownMbps.Location = new System.Drawing.Point(85, yPos - 3);
                txtDownMbps.Size = new System.Drawing.Size(100, 20);
                txtDownMbps.Text = "100";
                panVmess.Controls.Add(txtDownMbps);

                if (eConfigType == EConfigType.Hysteria2)
                {
                    yPos += 25;
                    lblObfs = new Label();
                    lblObfs.Text = "Obfs:";
                    lblObfs.Location = new System.Drawing.Point(10, yPos);
                    lblObfs.Size = new System.Drawing.Size(70, 20);
                    panVmess.Controls.Add(lblObfs);

                    cmbObfs = new ComboBox();
                    cmbObfs.Location = new System.Drawing.Point(85, yPos - 3);
                    cmbObfs.Size = new System.Drawing.Size(100, 20);
                    cmbObfs.Items.AddRange(new object[] { "salamander", "" });
                    cmbObfs.Text = "";
                    panVmess.Controls.Add(cmbObfs);

                    yPos += 25;
                    lblObfsPassword = new Label();
                    lblObfsPassword.Text = "Obfs Pass:";
                    lblObfsPassword.Location = new System.Drawing.Point(10, yPos);
                    lblObfsPassword.Size = new System.Drawing.Size(70, 20);
                    panVmess.Controls.Add(lblObfsPassword);

                    txtObfsPassword = new TextBox();
                    txtObfsPassword.Location = new System.Drawing.Point(85, yPos - 3);
                    txtObfsPassword.Size = new System.Drawing.Size(150, 20);
                    panVmess.Controls.Add(txtObfsPassword);
                }

                if (eConfigType == EConfigType.TUIC || eConfigType == EConfigType.Hysteria2)
                {
                    yPos += 25;
                    lblCertSha256 = new Label();
                    lblCertSha256.Text = "Cert SHA256:";
                    lblCertSha256.Location = new System.Drawing.Point(10, yPos);
                    lblCertSha256.Size = new System.Drawing.Size(70, 20);
                    panVmess.Controls.Add(lblCertSha256);

                    txtCertSha256 = new TextBox();
                    txtCertSha256.Location = new System.Drawing.Point(85, yPos - 3);
                    txtCertSha256.Size = new System.Drawing.Size(250, 20);
                    panVmess.Controls.Add(txtCertSha256);
                }
            }
        }

        private void AddServerForm_Load(object sender, EventArgs e)
        {
            Text = (eConfigType).ToString();
            
            cmbCoreType.Items.AddRange(Global.coreTypes.ToArray());
            cmbCoreType.Items.Add(string.Empty);

            switch (eConfigType)
            {
                case EConfigType.VMess:
                    panVmess.Dock = DockStyle.Fill;
                    panVmess.Visible = true;

                    cmbSecurity.Items.AddRange(Global.vmessSecuritys.ToArray());
                    break;
                case EConfigType.Shadowsocks:
                    panSs.Dock = DockStyle.Fill;
                    panSs.Visible = true;
                    //panTran.Visible = false;
                    //this.Height = this.Height - panTran.Height;

                    cmbSecurity3.Items.AddRange(LazyConfig.Instance.GetShadowsocksSecuritys(vmessItem).ToArray());
                    break;
                case EConfigType.Socks:
                    panSocks.Dock = DockStyle.Fill;
                    panSocks.Visible = true;
                    panTran.Visible = false;
                    Height = Height - panTran.Height;
                    break;
                case EConfigType.VLESS:
                    panVless.Dock = DockStyle.Fill;
                    panVless.Visible = true;
                    transportControl.AllowXtls = true;

                    cmbFlow5.Items.AddRange(Global.xtlsFlows.ToArray());
                    break;
                case EConfigType.Trojan:
                    panTrojan.Dock = DockStyle.Fill;
                    panTrojan.Visible = true;
                    transportControl.AllowXtls = true;

                    cmbFlow6.Items.AddRange(Global.xtlsFlows.ToArray());
                    break;
                case EConfigType.Hysteria2:
                case EConfigType.Mieru:
                case EConfigType.TUIC:
                    panVmess.Dock = DockStyle.Fill;
                    panVmess.Visible = true;
                    break;
            }

            if (vmessItem != null)
            {
                BindingServer();
            }
            else
            {
                vmessItem = new VmessItem
                {
                    groupId = groupId
                };
                ClearServer();
            }
        }

        /// <summary>
        /// 绑定数据
        /// </summary>
        private void BindingServer()
        {
            txtRemarks.Text = vmessItem.remarks;
            txtAddress.Text = vmessItem.address;
            txtPort.Text = vmessItem.port.ToString();

            switch (eConfigType)
            {
                case EConfigType.VMess:
                    txtId.Text = vmessItem.id;
                    txtAlterId.Text = vmessItem.alterId.ToString();
                    cmbSecurity.Text = vmessItem.security;
                    break;
                case EConfigType.Shadowsocks:
                    txtId3.Text = vmessItem.id;
                    cmbSecurity3.Text = vmessItem.security;
                    break;
                case EConfigType.Socks:
                    txtId4.Text = vmessItem.id;
                    txtSecurity4.Text = vmessItem.security;
                    break;
                case EConfigType.VLESS:
                    txtId5.Text = vmessItem.id;
                    cmbFlow5.Text = vmessItem.flow;
                    cmbSecurity5.Text = vmessItem.security;
                    break;
                case EConfigType.Trojan:
                    txtId6.Text = vmessItem.id;
                    cmbFlow6.Text = vmessItem.flow;
                    break;
                case EConfigType.Hysteria2:
                case EConfigType.Mieru:
                case EConfigType.TUIC:
                    txtId.Text = vmessItem.id;
                    if (txtUpMbps != null) txtUpMbps.Text = vmessItem.upMbps?.ToString() ?? "100";
                    if (txtDownMbps != null) txtDownMbps.Text = vmessItem.downMbps?.ToString() ?? "100";
                    if (txtObfsPassword != null) txtObfsPassword.Text = vmessItem.obfsPassword ?? "";
                    if (cmbObfs != null) cmbObfs.Text = vmessItem.obfs ?? "";
                    if (txtCertSha256 != null) txtCertSha256.Text = vmessItem.certSha256 ?? "";
                    break;
            }

            cmbCoreType.Text = vmessItem.coreType == null ? string.Empty : vmessItem.coreType.ToString();

            transportControl.BindingServer(vmessItem);
        }

        /// <summary>
        /// 清除设置
        /// </summary>
        private void ClearServer()
        {
            txtRemarks.Text = "";
            txtAddress.Text = "";
            txtPort.Text = "";

            switch (eConfigType)
            {
                case EConfigType.VMess:
                    txtId.Text = "";
                    txtAlterId.Text = "0";
                    cmbSecurity.Text = Global.DefaultSecurity;
                    break;
                case EConfigType.Shadowsocks:
                    txtId3.Text = "";
                    cmbSecurity3.Text = Global.DefaultSecurity;
                    break;
                case EConfigType.Socks:
                    txtId4.Text = "";
                    txtSecurity4.Text = "";
                    break;
                case EConfigType.VLESS:
                    txtId5.Text = "";
                    cmbFlow5.Text = "";
                    cmbSecurity5.Text = Global.None;
                    break;
                case EConfigType.Trojan:
                    txtId6.Text = "";
                    cmbFlow6.Text = "";
                    break;
            }

            transportControl.ClearServer(vmessItem);
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            string remarks = txtRemarks.Text;
            string address = txtAddress.Text;
            string port = txtPort.Text;

            string id = string.Empty;
            string alterId = string.Empty;
            string security = string.Empty;
            string flow = string.Empty;

            switch (eConfigType)
            {
                case EConfigType.VMess:
                    id = txtId.Text;
                    alterId = txtAlterId.Text;
                    security = cmbSecurity.Text;
                    break;
                case EConfigType.Shadowsocks:
                    id = txtId3.Text;
                    security = cmbSecurity3.Text;
                    break;
                case EConfigType.Socks:
                    id = txtId4.Text;
                    security = txtSecurity4.Text;
                    break;
                case EConfigType.VLESS:
                    id = txtId5.Text;
                    flow = cmbFlow5.Text;
                    security = cmbSecurity5.Text;
                    break;
                case EConfigType.Trojan:
                    id = txtId6.Text;
                    flow = cmbFlow6.Text;
                    break;
                case EConfigType.Hysteria2:
                case EConfigType.Mieru:
                case EConfigType.TUIC:
                    id = txtId.Text;
                    break;
            }

            if (Utils.IsNullOrEmpty(address))
            {
                UI.Show(ResUI.FillServerAddress);
                return;
            }
            if (Utils.IsNullOrEmpty(port) || !Utils.IsNumberic(port))
            {
                UI.Show(ResUI.FillCorrectServerPort);
                return;
            }
            if (eConfigType == EConfigType.Shadowsocks)
            {
                if (Utils.IsNullOrEmpty(id))
                {
                    UI.Show(ResUI.FillPassword);
                    return;
                }
                if (Utils.IsNullOrEmpty(security))
                {
                    UI.Show(ResUI.PleaseSelectEncryption);
                    return;
                }
            }
            if (eConfigType != EConfigType.Socks)
            {
                if (Utils.IsNullOrEmpty(id))
                {
                    UI.Show(ResUI.FillUUID);
                    return;
                }
            }

            transportControl.EndBindingServer();

            vmessItem.remarks = remarks;
            vmessItem.address = address;
            vmessItem.port = Utils.ToInt(port);
            vmessItem.id = id;
            vmessItem.alterId = Utils.ToInt(alterId);
            vmessItem.security = security;

            if (Utils.IsNullOrEmpty(cmbCoreType.Text))
            {
                vmessItem.coreType = null;
            }
            else
            {
                vmessItem.coreType = (ECoreType)Enum.Parse(typeof(ECoreType), cmbCoreType.Text);
            }

            if (eConfigType == EConfigType.Hysteria2 || eConfigType == EConfigType.Mieru || eConfigType == EConfigType.TUIC)
            {
                if (txtUpMbps != null && !string.IsNullOrEmpty(txtUpMbps.Text))
                    vmessItem.upMbps = Utils.ToInt(txtUpMbps.Text);
                if (txtDownMbps != null && !string.IsNullOrEmpty(txtDownMbps.Text))
                    vmessItem.downMbps = Utils.ToInt(txtDownMbps.Text);
                if (txtObfsPassword != null)
                    vmessItem.obfsPassword = txtObfsPassword.Text;
                if (cmbObfs != null)
                    vmessItem.obfs = cmbObfs.Text;
                if (txtCertSha256 != null)
                    vmessItem.certSha256 = txtCertSha256.Text;
            }

            int ret = -1;
            switch (eConfigType)
            {
                case EConfigType.VMess:
                    ret = ConfigHandler.AddServer(ref config, vmessItem);
                    break;
                case EConfigType.Shadowsocks:
                    ret = ConfigHandler.AddShadowsocksServer(ref config, vmessItem);
                    break;
                case EConfigType.Socks:
                    ret = ConfigHandler.AddSocksServer(ref config, vmessItem);
                    break;
                case EConfigType.VLESS:
                    vmessItem.flow = flow;
                    ret = ConfigHandler.AddVlessServer(ref config, vmessItem);
                    break;
                case EConfigType.Trojan:
                    vmessItem.flow = flow;
                    ret = ConfigHandler.AddTrojanServer(ref config, vmessItem);
                    break;
                case EConfigType.Hysteria2:
                case EConfigType.Mieru:
                case EConfigType.TUIC:
                    ret = ConfigHandler.AddServer(ref config, vmessItem);
                    break;
            }

            if (ret == 0)
            {
                DialogResult = DialogResult.OK;
            }
            else
            {
                UI.ShowWarning(ResUI.OperationFailed);
            }

        }

        private void btnGUID_Click(object sender, EventArgs e)
        {
            txtId.Text =
            txtId5.Text = Utils.GetGUID();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }
}
