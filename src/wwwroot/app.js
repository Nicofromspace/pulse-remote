'use strict';
const $=s=>document.querySelector(s), el=(tag,cls,text)=>{const n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=text;return n};
function stored(key,fallback){try{return JSON.parse(localStorage.getItem(key))??fallback}catch{return fallback}}
function save(key,value){try{localStorage.setItem(key,JSON.stringify(value))}catch{}}
let key=stored('pulse-key',''), prefs=stored('pulse-prefs',{fahrenheit:false,compact:false}), selected=stored('pulse-selection',null);
if(!Array.isArray(selected))selected=null;
if(!prefs||typeof prefs!=='object')prefs={fahrenheit:false,compact:false};
prefs={theme:'vector',accent:'default',graphs:true,stats:true,columns:'auto',history:2,refresh:2,...prefs};
if(!['vector','carbon','paper','arcade','pitlane','aurora'].includes(prefs.theme))prefs.theme='vector';
if(![1,2,5].includes(prefs.history))prefs.history=2;
if(![1,2,5,10].includes(prefs.refresh))prefs.refresh=2;
document.documentElement.classList.toggle('standalone',navigator.standalone===true);
function applyTheme(){
 const colors={vector:{default:'#82e9dd',amber:'#f0bb7a',violet:'#bdb0fb',lime:'#b9e580'},carbon:{default:'#eeb884',amber:'#eeb884',violet:'#c4b5fd',lime:'#bdce91'},arcade:{default:'#b8f873',amber:'#ffcc70',violet:'#d1b1ff',lime:'#b8f873'},pitlane:{default:'#ae202b',amber:'#8c4c12',violet:'#655091',lime:'#4b661f'},aurora:{default:'#d4b5ff',amber:'#f5c28b',violet:'#d4b5ff',lime:'#c6eca3'},paper:{default:'#31634c',amber:'#8c4c12',violet:'#655091',lime:'#4b661f'}};
 document.body.dataset.theme=prefs.theme;document.body.style.setProperty('--accent-color',colors[prefs.theme][prefs.accent]||colors[prefs.theme].default);
 document.body.classList.toggle('hide-graphs',!prefs.graphs);document.body.classList.toggle('hide-stats',!prefs.stats);document.body.classList.toggle('single-column',prefs.columns==='one');
 $('#theme-caption').textContent={vector:'VECTOR / TELEMETRY CONSOLE',carbon:'CARBON / HARDWARE MONITOR',paper:'PAPER / SYSTEM OBSERVATORY',arcade:'ARCADE / SYSTEM ONLINE',pitlane:'PITLANE / LIVE TELEMETRY',aurora:'AURORA / YOUR SYSTEM, IN VIEW'}[prefs.theme];
 document.querySelector('meta[name=theme-color]').content={vector:'#090f13',carbon:'#111111',paper:'#f1f0e9',arcade:'#10160e',pitlane:'#ececea',aurora:'#151226'}[prefs.theme];
}
const fragment=new URLSearchParams(location.hash.slice(1));if(fragment.has('key')){key=fragment.get('key');save('pulse-key',key)}
function rememberShortcut(){
 if(!/^[a-f0-9]{32}$/i.test(key))return;
 // Fragments stay in the browser, never in HTTP requests. Preserve pairing in Home Screen launches.
 const start=location.pathname+'#key='+key;history.replaceState(null,'',start);
 const manifest={name:'Pulse Remote',short_name:'Pulse',start_url:location.origin+start,scope:location.origin+'/',display:'standalone',background_color:'#0c1118',theme_color:'#0c1118',icons:[{src:location.origin+'/icon.svg',sizes:'any',type:'image/svg+xml'}]};
 const link=document.querySelector('link[rel=manifest]');if(link.href.startsWith('blob:'))URL.revokeObjectURL(link.href);link.href=URL.createObjectURL(new Blob([JSON.stringify(manifest)],{type:'application/manifest+json'}));
}
rememberShortcut();
let readings=[],latest=new Map(),histories=new Map(),online=false,lastPoll=null,lastReceived=0,view='',detailId='',busy=false;
let pollTimer;
function schedulePoll(){clearInterval(pollTimer);pollTimer=setInterval(poll,prefs.refresh*1000);$('.interval').textContent=prefs.refresh+'s refresh'}
const types={1:['Temperature','#e5b984'],2:['Voltage','#c3b0fb'],3:['Cooling','#8edcd5'],4:['Current','#b4c8ff'],5:['Power','#e5b984'],6:['Clock','#aeb4ff'],7:['Utilization','#8be2be'],8:['Sensor','#8abcf1']};
function isTemp(r){return r.type===1&&/^(°?C|°?F)$/i.test(r.unit)}
function number(r,v){if(v===null||v===undefined||!Number.isFinite(v))return '—';if(isTemp(r)){if(prefs.fahrenheit&&!/F/i.test(r.unit))v=v*9/5+32;if(!prefs.fahrenheit&&/F/i.test(r.unit))v=(v-32)*5/9}return v.toLocaleString(undefined,{maximumFractionDigits:Math.abs(v)<10?2:1})}
function unit(r){return isTemp(r)?(prefs.fahrenheit?'°F':'°C'):r.unit}
function value(r,v=r.value){return `${number(r,v)}${v===null?'':' '+unit(r)}`}
function persist(){save('pulse-selection',selected);save('pulse-prefs',prefs)}
function defaults(data){
 const pick=(pred)=>data.find(r=>r.value!==null&&pred(r)), out=[];
 const add=r=>{if(r&&!out.includes(r.id))out.push(r.id)};
 add(pick(r=>/cpu/i.test(r.group)&&r.type===1&&/Tctl|package|cpu die|cpu temperature/i.test(r.name)));
 add(pick(r=>/cpu/i.test(r.group)&&r.type===7&&/total cpu usage|total cpu utility/i.test(r.name)));
 add(pick(r=>/nvidia|radeon.*rx|geforce/i.test(r.group)&&r.type===1&&/^gpu temperature$/i.test(r.name)));
 add(pick(r=>/nvidia|radeon.*rx|geforce/i.test(r.group)&&r.type===7&&/^gpu core load|^gpu utilization/i.test(r.name)));
 add(pick(r=>/physical memory load/i.test(r.name)));
 add(pick(r=>r.type===1&&/drive temperature/i.test(r.name)));
 for(const r of data.filter(r=>r.type===1||r.type===7)){if(out.length>=6)break;add(r)}
 return out;
}
function chart(r,large=false){
 const box=el('div',large?'detail-chart':'');const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox','0 0 280 70');svg.setAttribute('preserveAspectRatio','none');svg.setAttribute('aria-label','Recent reading history');svg.classList.add('chart');
 const windowMs=prefs.history*60000;const points=(histories.get(r.id)||[]).filter(p=>Date.now()-p.t<=windowMs);const vals=points.filter(p=>p.v!==null).map(p=>p.v);let low=Math.min(...vals),high=Math.max(...vals);
 if(r.unit==='%'){low=0;high=100}else if(!vals.length){low=0;high=1}else{const pad=Math.max((high-low)*.15,Math.abs(high)*.02,1);low-=pad;high+=pad}
 const base=document.createElementNS(svg.namespaceURI,'path');base.setAttribute('d','M0 65H280');base.classList.add('baseline');svg.append(base);
 const grid=document.createElementNS(svg.namespaceURI,'path');grid.setAttribute('d','M0 20H280 M0 42H280 M70 0V65 M140 0V65 M210 0V65');grid.classList.add('chart-grid');svg.append(grid);
 let d='',gap=true;const now=Date.now();
 for(const p of points){if(p.v===null){gap=true;continue}const x=Math.max(0,280-(now-p.t)/windowMs*280),y=62-(p.v-low)/(high-low)*54;d+=`${gap?'M':'L'}${x.toFixed(1)} ${y.toFixed(1)} `;gap=false}
 const path=document.createElementNS(svg.namespaceURI,'path');path.setAttribute('d',d);svg.append(path);box.append(svg);
 const labels=el('div','chart-label');labels.append(el('span','', 'LAST '+prefs.history+' MIN'),el('span','', 'NOW'));box.append(labels);return box;
}
function render(){
 applyTheme();
 document.body.classList.toggle('compact',!!prefs.compact);
 $('#pairing').hidden=!!key;$('#cards').replaceChildren();
 const ids=selected||[];
 $('#card-count').textContent=String(ids.length).padStart(2,'0');
 $('#empty').hidden=ids.length>0||!online;
 for(const id of ids){
  const r=latest.get(id);if(!r)continue;
  const card=el('button','card');card.style.setProperty('--accent',(types[r.type]||types[8])[1]);
  const top=el('div','card-top'),tag=el('span','tag');tag.append(el('span','card-index',String(ids.indexOf(id)+1).padStart(2,'0')),document.createTextNode((types[r.type]||types[8])[0]));top.append(tag,el('span','arrow','↗'));
  const title=el('div','sensor-name',r.name);title.title=r.name;
  const num=el('div','number',online?number(r,r.value):'—');num.append(el('small','',unit(r)));
  const range=el('div','range');range.append(el('span','',online?`MIN ${number(r,r.minimum)}`:'Not live'),el('span','',online?`MAX ${number(r,r.maximum)}`:''));
  card.append(top,title,el('div','group-name',r.group),num,range);
  if(r.unit==='%'&&online&&r.value!==null){const meter=el('div','meter'),fill=el('div','meter-fill');fill.style.width=Math.max(0,Math.min(100,r.value))+'%';meter.append(fill);card.append(meter)}
  card.append(chart(r));card.onclick=()=>openDetail(id);$('#cards').append(card);
 }
 if(!online&&!ids.length){$('#cards').append(el('div','empty','Your live cards will appear when HWiNFO sharing is available.'))}
 $('#sensor-count').textContent=readings.length?`${readings.length} readings available. Find your favorites.`:'Your HWiNFO sensors, all in one place.';
 if(view==='detail')renderDetail();
 for(const n of document.querySelectorAll('.sensor-row[data-id]')){const r=latest.get(n.dataset.id),v=n.querySelector('.reading');if(r&&v)v.textContent=online?value(r):'—'}
}
function message(status,text){online=status==='live';$('#dot').classList.toggle('wait',!online);$('#status').textContent=online?'Connected · live from your PC':status==='offline'?'PC connection lost':'Waiting for readings';$('#notice').hidden=online||!key;$('#notice').textContent=text}
async function poll(){
 if(busy||document.hidden||!key)return;busy=true;
 try{
  const res=await fetch('/api/snapshot',{headers:{'X-Pulse-Key':key},cache:'no-store',signal:AbortSignal.timeout(6000)});
  if(res.status===401){key='';save('pulse-key','');throw Error('Pair again using the QR code on your PC.')}
  if(!res.ok)throw Error(res.status===429?'Too many requests. Retrying shortly.':'PC is not responding.');
  const data=await res.json();lastReceived=Date.now();message(data.status,data.message);
  if([1,2,5,10].includes(data.refreshSeconds)&&prefs.refresh!==data.refreshSeconds){prefs.refresh=data.refreshSeconds;persist();schedulePoll()}
  if(online){
   readings=data.readings;for(const r of readings)latest.set(r.id,r);
   const present=new Set(readings.map(r=>r.id));for(const [id,r]of latest)if(!present.has(id))latest.set(id,{...r,value:null,minimum:null,maximum:null,average:null});
   if(selected===null){selected=defaults(readings);persist()}
   if(data.pollTime!==lastPoll){
    const now=Date.now();for(const r of readings){let points=histories.get(r.id)||[];points.push({t:now,v:r.value});points=points.filter(p=>now-p.t<=300000);histories.set(r.id,points)}
    for(const id of histories.keys())if(!present.has(id))histories.delete(id);
    lastPoll=data.pollTime;
   }
  }else{for(const points of histories.values())if(points.at(-1)?.v!==null)points.push({t:Date.now(),v:null});lastPoll=null}
 }catch(e){message('offline',e.name==='TimeoutError'?'Your PC is not responding. Check that it is awake and Pulse Remote is running.':e.message);for(const points of histories.values())if(points.at(-1)?.v!==null)points.push({t:Date.now(),v:null});lastPoll=null}
 finally{busy=false;render()}
}
function openSheet(title,kicker,type){view=type;$('#sheet-title').textContent=title;$('#sheet-kicker').textContent=kicker;$('#sheet-body').replaceChildren();if(!$('#sheet').open)$('#sheet').showModal()}
function closeSheet(){$('#sheet').close();view=''}
$('#close-sheet').onclick=closeSheet;$('#sheet').addEventListener('close',()=>view='');$('#sheet').addEventListener('click',e=>{if(e.target===$('#sheet')){const r=e.target.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)closeSheet()}});
function row(r){const n=el('div','sensor-row'),copy=el('div','copy');n.dataset.id=r.id||'';copy.append(el('strong','',r.name),el('small','',r.group));n.append(copy);return n}
function manage(){
 openSheet('Make it yours.','YOUR DASHBOARD','manage');const body=$('#sheet-body');body.append(el('p','help','Choose any reading. Use the arrows to rearrange your cards. Your layout is saved on this device.'));
 const order=el('div');body.append(order);
 function drawOrder(){order.replaceChildren();for(const [i,id] of (selected||[]).entries()){
  const r=latest.get(id)||{name:'Unavailable sensor',group:id};const n=row(r),actions=el('div','row-actions');
  for(const [label,delta]of [['↑',-1],['↓',1],['×',0]]){const b=el('button','',label);b.setAttribute('aria-label',`${delta===0?'Remove':delta<0?'Move up':'Move down'} ${r.name}`);b.disabled=delta===-1&&i===0||delta===1&&i===selected.length-1;b.onclick=()=>{if(!delta)selected.splice(i,1);else[selected[i],selected[i+delta]]=[selected[i+delta],selected[i]];persist();drawOrder();drawList();render()};actions.append(b)}n.append(actions);order.append(n)
 }}
 const label=el('label','','Find a sensor');label.htmlFor='search';const input=el('input');input.id='search';input.placeholder='Try CPU, GPU, fan, memory…';input.type='search';const list=el('div');body.append(label,input,list);
 function drawList(){list.replaceChildren();const q=input.value.toLowerCase();for(const r of readings.filter(r=>(r.group+' '+r.name).toLowerCase().includes(q))){
  const n=row(r),check=el('input');check.type='checkbox';check.checked=(selected||[]).includes(r.id);check.setAttribute('aria-label',`Show ${r.name} from ${r.group}`);check.onchange=()=>{selected=selected||[];if(check.checked&&!selected.includes(r.id))selected.push(r.id);else if(!check.checked)selected=selected.filter(id=>id!==r.id);persist();drawOrder();render()};n.prepend(check);n.append(el('span','reading',online?value(r):'—'));list.append(n)
 }if(!readings.length)list.append(el('p','help','Connect HWiNFO to choose sensors.'))}
 input.oninput=drawList;drawOrder();drawList();
}
function browse(){
 openSheet('Every sensor.','DIRECT FROM HWiNFO','browse');const input=el('input');input.type='search';input.placeholder='Search readings…';input.setAttribute('aria-label','Search all sensors');const list=el('div');$('#sheet-body').append(input,list);
 function draw(){list.replaceChildren();for(const r of readings.filter(r=>(r.group+' '+r.name).toLowerCase().includes(input.value.toLowerCase()))){const n=row(r);n.append(el('span','reading',online?value(r):'—'));const b=el('button','','↗');b.setAttribute('aria-label',`Details for ${r.name}`);b.onclick=()=>openDetail(r.id);n.append(b);list.append(n)}if(!readings.length)list.append(el('p','help','Waiting for HWiNFO readings.'))}input.oninput=draw;draw();
}
function openDetail(id){detailId=id;openSheet(latest.get(id)?.name||'Sensor','A CLOSER LOOK','detail');renderDetail()}
function renderDetail(){const r=latest.get(detailId);if(!r)return;const body=$('#sheet-body');body.replaceChildren(el('p','help',r.group),el('div','detail-value',online?value(r):'Not available'));
 const stats=el('div','detail-stats');for(const [name,v]of [['Minimum',r.minimum],['Average',r.average],['Maximum',r.maximum]]){const n=el('div');n.append(el('small','',name),el('span','',online?value(r,v):'—'));stats.append(n)}body.append(stats,chart(r,true),el('p','help','Statistics come from HWiNFO’s current monitoring session. The graph shows readings received on this device.'));
 const b=el('button','primary',(selected||[]).includes(r.id)?'Remove from dashboard':'Add to dashboard');b.onclick=()=>{selected=(selected||[]).includes(r.id)?selected.filter(id=>id!==r.id):[...(selected||[]),r.id];persist();render()};body.append(b);
}
function settings(){
 openSheet('Your control panel.','PREFERENCES / SAVED AUTOMATICALLY','settings');const body=$('#sheet-body');
 body.append(el('h3','subheading','Interface skin'));const picker=el('div','theme-picker');
 for(const [id,title,desc]of [['vector','Vector','Instrument panel'],['carbon','Carbon','Quiet & minimal'],['paper','Paper','Light & precise'],['arcade','Arcade','Retro terminal'],['pitlane','Pitlane','Track inspired'],['aurora','Aurora','Soft & luminous']]){
  const b=el('button','theme-choice');b.dataset.themeChoice=id;b.setAttribute('aria-pressed',String(prefs.theme===id));b.setAttribute('aria-label',title+' theme');b.append(el('div','skin-sample sample-'+id,'48.2°'),el('strong','',title),el('small','',desc));b.onclick=()=>{prefs.theme=id;persist();render();for(const n of picker.children)n.setAttribute('aria-pressed',String(n.dataset.themeChoice===id))};picker.append(b)
 }body.append(picker);const grid=el('div','settings-grid');body.append(grid);
 function selectOption(id,label,options,current,change){const wrap=el('div'),l=el('label','',label),s=el('select');l.htmlFor=id;s.id=id;for(const [v,t]of options){const o=el('option','',t);o.value=v;s.append(o)}s.value=current;s.onchange=()=>change(s);wrap.append(l,s);grid.append(wrap);return s}
 selectOption('accent','Accent',[['default','Skin default'],['amber','Amber'],['violet','Violet'],['lime','Lime']],prefs.accent,s=>{prefs.accent=s.value;persist();render()});
 selectOption('units','Temperature',[['c','Celsius · °C'],['f','Fahrenheit · °F']],prefs.fahrenheit?'f':'c',s=>{prefs.fahrenheit=s.value==='f';persist();render()});
 const feedback=el('p','settings-message');feedback.setAttribute('role','status');
 selectOption('refresh','Refresh rate',[['1','1 second'],['2','2 seconds'],['5','5 seconds'],['10','10 seconds']],String(prefs.refresh),async s=>{
  s.disabled=true;feedback.textContent='Saving refresh rate…';try{const res=await fetch('/api/refresh/'+s.value,{method:'POST',headers:{'X-Pulse-Key':key},signal:AbortSignal.timeout(6000)});if(!res.ok)throw Error('Could not save. Check your PC connection.');const d=await res.json();prefs.refresh=d.refreshSeconds;persist();schedulePoll();feedback.textContent='Saved · applies to all connected dashboards.'}catch(e){s.value=String(prefs.refresh);feedback.textContent=e.message}finally{s.disabled=false}
 });
 selectOption('history','Graph history',[['1','1 minute'],['2','2 minutes'],['5','5 minutes']],String(prefs.history),s=>{prefs.history=Number(s.value);persist();render()});
 selectOption('columns','Card layout',[['auto','Two columns'],['one','One wide column']],prefs.columns,s=>{prefs.columns=s.value;persist();render()});
 body.append(feedback,el('p','help','Refresh changes both Pulse’s PC reader and phone updates. HWiNFO controls how often sensors produce new readings; faster refresh cannot create extra samples. Slower refresh reduces Pulse’s work.'));
 function toggle(id,title,description,checked,change){const l=el('label','settings-row'),copy=el('span','',title),c=el('input');if(description)copy.append(el('small','',description));c.type='checkbox';c.id=id;c.checked=checked;c.onchange=()=>{change(c.checked);persist();render()};l.append(copy,c);body.append(l)}
 toggle('graphs','Live graphs','Recent activity on each card',prefs.graphs,v=>prefs.graphs=v);
 toggle('stats','Minimum / maximum','HWiNFO session statistics',prefs.stats,v=>prefs.stats=v);
 toggle('compact','Compact cards','Smaller cards with graphs hidden',!!prefs.compact,v=>prefs.compact=v);
 body.append(el('p','help','Appearance and layout are saved on this device. The bottom navigation also opens Settings when using the Home Screen app.'));
 body.append(el('h3','subheading','Home Screen & connection'),el('p','help','Use Safari → Share → Add to Home Screen. If an older shortcut keeps the previous status-bar appearance, remove that shortcut and add it again. HWiNFO Free still requires manually re-enabling sharing after its 12-hour limit.'));
 const reset=el('button','primary','Restore suggested cards');reset.onclick=()=>{selected=readings.length?defaults(readings):null;persist();render();closeSheet()};body.append(reset);
}
$('#customize').onclick=manage;$('#empty-add').onclick=manage;$('#browse').onclick=browse;$('#settings').onclick=settings;
$('#nav-settings').onclick=settings;$('#nav-edit').onclick=manage;$('#nav-sensors').onclick=browse;$('#nav-monitor').onclick=()=>window.scrollTo({top:0,behavior:'smooth'});
$('#pair-form').onsubmit=e=>{e.preventDefault();const text=$('#pair-key').value.trim();try{key=new URL(text).hash.split('key=')[1]||''}catch{key=text}if(!/^[a-f0-9]{32}$/i.test(key)){message('offline','Paste the full phone link from Pulse Remote.');$('#notice').hidden=false;return}save('pulse-key',key);rememberShortcut();poll()};
document.addEventListener('visibilitychange',()=>{if(!document.hidden){if(Date.now()-lastReceived>10000){message('offline','Reconnecting…');render()}poll()}});
schedulePoll();render();poll();
