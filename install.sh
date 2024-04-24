#!/usr/bin/env bash
DEST="/home/matrix"

apt-get update
apt-get install -y wget unzip

# Install .NET
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh 
chmod 755 dotnet-install.sh
./dotnet-install.sh --channel 8.0 --runtime aspnetcore

# Download zip
mkdir $DEST -p 
cd $DEST
wget https://github.com/immisterio/MatriX.API/releases/latest/download/publish.zip
unzip -o publish.zip
rm -f publish.zip

# Create service
echo ""
echo "Install service to /etc/systemd/system/matrix.service ..."
touch /etc/systemd/system/matrix.service && chmod 664 /etc/systemd/system/matrix.service
cat <<EOF > /etc/systemd/system/matrix.service
[Unit]
Description=matrix
Wants=network.target
After=network.target
[Service]
WorkingDirectory=$DEST
ExecStart=$HOME/.dotnet/dotnet MatriX.API.dll
#ExecReload=/bin/kill -s HUP $MAINPID
#ExecStop=/bin/kill -s QUIT $MAINPID
Restart=always
[Install]
WantedBy=multi-user.target
EOF

# Enable service
systemctl daemon-reload
systemctl enable matrix
systemctl start matrix

# iptables drop
cat <<EOF > iptables-drop.sh
#!/bin/sh
echo "Stopping firewall and allowing everyone..."
iptables -F
iptables -X
iptables -t nat -F
iptables -t nat -X
iptables -t mangle -F
iptables -t mangle -X
iptables -P INPUT ACCEPT
iptables -P FORWARD ACCEPT
iptables -P OUTPUT ACCEPT
EOF

# Note
echo ""
echo "################################################################"
echo ""
echo "Have fun!"
echo ""
echo "Then [re]start it as systemctl [re]start matrix"
echo ""
echo "Clear iptables if port 8090 is not available"
echo "bash $DEST/iptables-drop.sh"
echo ""
