#!/usr/bin/env node
// vmrun.exe taklidi: durum dosyada tutulur.
const fs = require('fs');
const state = process.env.FAKE_VMRUN_STATE;
const running = fs.existsSync(state) ? fs.readFileSync(state, 'utf8').split('\n').filter(Boolean) : [];
const args = process.argv.slice(2).filter((a) => a !== '-T' && a !== 'ws');
const [cmd, vmx] = args;
if (cmd === 'list') { console.log(`Total running VMs: ${running.length}`); running.forEach((r) => console.log(r)); }
else if (cmd === 'start') { fs.writeFileSync(state, [...running, vmx].join('\n')); }
else if (cmd === 'getGuestIPAddress') { console.log(running.includes(vmx) ? '192.168.56.10' : 'Error: The virtual machine is not powered on'); }
else if (cmd === 'suspend') { fs.writeFileSync(state, running.filter((r) => r !== vmx).join('\n')); }
else console.log('Error: Unknown command');
