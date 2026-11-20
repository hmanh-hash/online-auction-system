// =====================================================================
//  Online Auction – WebClient (Vanilla JS + SignalR)
// =====================================================================
const API = ((window.APP_CONFIG && window.APP_CONFIG.apiBaseUrl) || 'http://localhost:5100').replace(/\/$/, '');

const state = {
  auctions: new Map(),   // id -> AuctionDto
  selectedId: null,
  bids: [],              // bids của phiên đang chọn (mới nhất trước)
};

// ---------- Helpers ---------------------------------------------------
const $ = (id) => document.getElementById(id);
const nf = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 });
const money = (v) => `${nf.format(Number(v ?? 0))} ₫`;
const fmtTime = (iso) => new Date(iso).toLocaleString('vi-VN');
const fmtClock = (iso) => new Date(iso).toLocaleTimeString('vi-VN');
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const sameName = (a, b) => (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase();

function toLocalInput(date) {
  const off = date.getTimezoneOffset();
  return new Date(date.getTime() - off * 60000).toISOString().slice(0, 19);
}

function formatRemaining(ms) {
  if (ms <= 0) return '00:00';
  const s = Math.floor(ms / 1000);
  const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  const pad = (n) => String(n).padStart(2, '0');
  if (d > 0) return `${d}n ${pad(h)}:${pad(m)}:${pad(sec)}`;
  return h > 0 ? `${pad(h)}:${pad(m)}:${pad(sec)}` : `${pad(m)}:${pad(sec)}`;
}

function countdownText(a) {
  const now = Date.now();
  if (a.status === 'Ended') return 'Đã kết thúc';
  const start = new Date(a.startTime).getTime();
  if (now < start) return `Bắt đầu sau ${formatRemaining(start - now)}`;
  return formatRemaining(new Date(a.endTime).getTime() - now);
}

const STATUS_STYLE = {
  Pending: ['bg-amber-100 text-amber-700', 'Chờ mở'],
  Active: ['bg-emerald-100 text-emerald-700', 'Đang diễn ra'],
  Ended: ['bg-slate-200 text-slate-600', 'Đã kết thúc'],
};
function statusBadge(status) {
  const [cls, label] = STATUS_STYLE[status] || STATUS_STYLE.Pending;
  return `<span class="px-2 py-0.5 rounded-full text-xs font-semibold ${cls}">${label}</span>`;
}

function toast(message, type = 'info') {
  const colors = { info: 'bg-slate-800', success: 'bg-emerald-600', error: 'bg-rose-600', warn: 'bg-amber-500' };
  const el = document.createElement('div');
  el.className = `${colors[type]} text-white px-4 py-3 rounded-lg shadow-lg max-w-sm slide-in text-sm`;
  el.textContent = message;
  $('toasts').appendChild(el);
  setTimeout(() => el.remove(), 4500);
}

function logActivity(html, icon = '•') {
  const li = document.createElement('li');
  li.className = 'slide-in border-l-4 border-indigo-300 pl-2';
  li.innerHTML = `<span class="text-slate-400 text-xs">${new Date().toLocaleTimeString('vi-VN')}</span> ${icon} ${html}`;
  $('activity').prepend(li);
  while ($('activity').children.length > 50) $('activity').lastChild.remove();
}

// ---------- REST ------------------------------------------------------
async function api(path, options = {}) {
  const res = await fetch(`${API}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  const text = await res.text();
  const body = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const msg = body?.message
      || (body?.errors && Object.values(body.errors).flat().join(' '))
      || body?.title
      || `HTTP ${res.status}`;
    const err = new Error(msg);
    err.status = res.status;
    err.code = body?.code;
    throw err;
  }
  return body;
}

// ---------- Identity (mỗi tab một tên khác nhau để giả lập nhiều user) ----
function initIdentity() {
  let me = sessionStorage.getItem('bidderName');
  if (!me) {
    me = 'User-' + Math.random().toString(36).slice(2, 6).toUpperCase();
    sessionStorage.setItem('bidderName', me);
  }
  $('myName').value = me;
  $('myName').addEventListener('change', (e) => {
    sessionStorage.setItem('bidderName', e.target.value.trim());
    renderDetail();
  });
}
const myName = () => $('myName').value.trim();

// ---------- Rendering -------------------------------------------------
function renderList() {
  const order = { Active: 0, Pending: 1, Ended: 2 };
  const items = [...state.auctions.values()]
    .sort((a, b) => (order[a.status] - order[b.status]) || (new Date(b.createdAt) - new Date(a.createdAt)));

  if (items.length === 0) {
    $('auctionList').innerHTML = '<p class="text-slate-400 text-sm">Chưa có phiên nào. Hãy tạo phiên mới!</p>';
    return;
  }

  $('auctionList').innerHTML = items.map((a) => `
    <div data-id="${a.id}" class="auction-card cursor-pointer rounded-xl border p-3 hover:border-indigo-400 transition
         ${a.id === state.selectedId ? 'border-indigo-500 bg-indigo-50 ring-1 ring-indigo-300' : 'border-slate-200'}">
      <div class="flex justify-between items-start gap-2">
        <div class="font-semibold truncate">${esc(a.productName)}</div>
        ${statusBadge(a.status)}
      </div>
      <div class="flex justify-between items-end mt-1">
        <div class="text-indigo-700 font-bold" data-price="${a.id}">${money(a.currentPrice)}</div>
        <div class="text-xs text-slate-500 font-mono" data-countdown="${a.id}">${countdownText(a)}</div>
      </div>
      <div class="text-xs text-slate-400 mt-1">${a.totalBids ?? 0} lượt · ${a.status === 'Ended'
        ? (a.winnerId ? '🏆 ' + esc(a.winnerId) : 'không có người thắng')
        : (a.highestBidder ? 'dẫn đầu: ' + esc(a.highestBidder) : 'chưa có lượt')}</div>
    </div>`).join('');

  document.querySelectorAll('.auction-card').forEach((el) =>
    el.addEventListener('click', () => selectAuction(el.dataset.id)));
}

function renderDetail() {
  const a = state.auctions.get(state.selectedId);
  $('emptyDetail').classList.toggle('hidden', !!a);
  $('detail').classList.toggle('hidden', !a);
  if (!a) return;

  $('dName').textContent = a.productName;
  $('dStatus').innerHTML = statusBadge(a.status);
  $('dDesc').textContent = a.description || '';
  $('dId').textContent = a.id;
  $('dPrice').textContent = money(a.currentPrice);
  $('dLeader').textContent = a.highestBidder || '—';
  $('dMinNext').textContent = money(a.minNextBid);
  $('dStep').textContent = money(a.minStepPrice);
  $('dTotal').textContent = a.totalBids ?? 0;
  $('dStart').textContent = money(a.startingPrice);
  $('dTimes').textContent = `${fmtTime(a.startTime)} → ${fmtTime(a.endTime)}`;
  $('dCountdown').textContent = countdownText(a);

  const active = a.status === 'Active';
  const leading = a.highestBidder && sameName(a.highestBidder, myName());
  $('bidBtn').disabled = !active && a.status !== 'Pending';
  $('endBtn').disabled = a.status === 'Ended';
  $('bidAmount').disabled = a.status === 'Ended';

  const banner = $('winnerBanner');
  if (a.status === 'Ended') {
    banner.classList.remove('hidden');
    if (a.winnerId) {
      const isMe = sameName(a.winnerId, myName());
      banner.className = `mt-6 rounded-xl p-4 text-center text-lg font-semibold ${isMe ? 'bg-yellow-100 text-yellow-800' : 'bg-emerald-50 text-emerald-800'}`;
      banner.innerHTML = isMe
        ? `🎉 Chúc mừng! Bạn đã thắng phiên đấu giá với giá <b>${money(a.currentPrice)}</b>`
        : `🏆 Người thắng: <b>${esc(a.winnerId)}</b> với giá <b>${money(a.currentPrice)}</b>`;
    } else {
      banner.className = 'mt-6 rounded-xl p-4 text-center text-lg font-semibold bg-slate-100 text-slate-600';
      banner.textContent = 'Phiên đã kết thúc mà không có lượt trả giá nào.';
    }
  } else if (leading) {
    banner.className = 'mt-6 rounded-xl p-3 text-center font-semibold bg-indigo-50 text-indigo-700';
    banner.textContent = '👑 Bạn đang là người trả giá cao nhất!';
  } else {
    banner.className = 'hidden';
  }
}

function renderBids(newBidId = null) {
  const rows = state.bids;
  $('noBids').classList.toggle('hidden', rows.length > 0);
  const maxAmount = Math.max(0, ...rows.map((b) => Number(b.amount)));
  $('bidRows').innerHTML = rows.map((b, i) => `
    <tr class="border-b last:border-0 ${b.id === newBidId ? 'flash' : ''} ${Number(b.amount) === maxAmount ? 'font-semibold text-emerald-700' : ''}">
      <td class="py-2 text-slate-400">${rows.length - i}</td>
      <td>${esc(b.bidderName)}${sameName(b.bidderName, myName()) ? ' <span class="text-xs text-indigo-500">(bạn)</span>' : ''}</td>
      <td class="text-right">${money(b.amount)}</td>
      <td class="text-right text-slate-500">${fmtClock(b.createdAt)}</td>
    </tr>`).join('');
}

function flashPrice() {
  const el = $('dPrice');
  el.classList.remove('flash');
  void el.offsetWidth; // restart animation
  el.classList.add('flash');
}

function mergeAuction(patch) {
  const existing = state.auctions.get(patch.id);
  if (!existing) return null;
  Object.assign(existing, patch);
  return existing;
}

function addBidLocal(bid) {
  if (state.bids.some((b) => b.id === bid.id)) return false;
  state.bids.unshift(bid);
  state.bids.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
  return true;
}

// ---------- Data loading ------------------------------------------------
async function loadAuctions() {
  try {
    const list = await api('/api/auctions');
    state.auctions = new Map(list.map((a) => [a.id, a]));
    renderList();
    renderDetail();
  } catch (e) {
    $('auctionList').innerHTML = `<p class="text-rose-600 text-sm">Không tải được danh sách: ${esc(e.message)}<br/>API: ${esc(API)}</p>`;
  }
}

async function loadSelected() {
  if (!state.selectedId) return;
  const [auction, bids] = await Promise.all([
    api(`/api/auctions/${state.selectedId}`),
    api(`/api/auctions/${state.selectedId}/bids`),
  ]);
  state.auctions.set(auction.id, auction);
  state.bids = bids;
  renderList();
  renderDetail();
  renderBids();
}

async function selectAuction(id) {
  if (state.selectedId === id) return;
  const prev = state.selectedId;
  state.selectedId = id;
  state.bids = [];
  $('activity').innerHTML = '';
  $('bidError').classList.add('hidden');

  if (connection.state === signalR.HubConnectionState.Connected) {
    if (prev) await connection.invoke('LeaveAuctionGroup', prev).catch(() => {});
    await connection.invoke('JoinAuctionGroup', id).catch((e) => toast('Không join được group: ' + e.message, 'error'));
  }
  history.replaceState(null, '', `#${id}`);
  await loadSelected().catch((e) => toast(e.message, 'error'));
  const a = state.auctions.get(id);
  if (a) $('bidAmount').value = a.minNextBid;
  logActivity(`Đã tham gia group theo dõi phiên <b>${esc(a?.productName)}</b>`, '👀');
}

// ---------- SignalR -----------------------------------------------------
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API}/hubs/auction`)
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .configureLogging(signalR.LogLevel.Warning)
  .build();

function setConn(text, cls) {
  $('connBadge').className = `px-3 py-1 rounded-full text-xs font-semibold ${cls}`;
  $('connBadge').textContent = text;
}

// Sự kiện theo group (chỉ phiên đang theo dõi)
connection.on('BidAccepted', (e) => {
  const a = mergeAuction({
    id: e.auctionId, currentPrice: e.amount, minNextBid: e.minNextBid,
    totalBids: e.totalBids, highestBidder: e.bidderName,
  });
  if (e.auctionId !== state.selectedId) return;

  addBidLocal({ id: e.bidId, auctionId: e.auctionId, bidderName: e.bidderName, amount: e.amount, createdAt: e.occurredAt });
  renderDetail();
  renderBids(e.bidId);
  flashPrice();

  const amountInput = $('bidAmount');
  if (!amountInput.value || Number(amountInput.value) < Number(e.minNextBid)) amountInput.value = e.minNextBid;

  const mine = sameName(e.bidderName, myName());
  logActivity(`<b>${esc(e.bidderName)}</b> trả <b class="text-emerald-700">${money(e.amount)}</b> (trước: ${money(e.previousPrice)})`, '💰');
  if (!mine) toast(`${e.bidderName} vừa trả ${money(e.amount)} cho "${a?.productName ?? e.productName}"`, 'warn');
});

connection.on('AuctionEnded', (e) => {
  mergeAuction({ id: e.auctionId, status: 'Ended', winnerId: e.winnerId, currentPrice: e.finalPrice, totalBids: e.totalBids, endTime: e.endedAt });
  if (e.auctionId !== state.selectedId) return;
  renderList();
  renderDetail();
  logActivity(`Phiên kết thúc (${esc(e.endedBy)}). Người thắng: <b>${esc(e.winnerId ?? 'không có')}</b> – ${money(e.finalPrice)}`, '🏁');
  toast(e.winnerId ? `Phiên kết thúc! Người thắng: ${e.winnerId} (${money(e.finalPrice)})` : 'Phiên kết thúc, không có người thắng.',
    sameName(e.winnerId, myName()) ? 'success' : 'info');
});

// Broadcast toàn cục (cập nhật danh sách)
connection.on('AuctionUpdated', (patch) => {
  if (!mergeAuction(patch)) return loadAuctions();
  renderList();
  if (patch.id === state.selectedId) renderDetail();
});

connection.on('AuctionCreated', (dto) => {
  state.auctions.set(dto.id, dto);
  renderList();
});

connection.on('AuctionStatusChanged', (patch) => {
  if (!mergeAuction(patch)) return loadAuctions();
  renderList();
  if (patch.id === state.selectedId) {
    renderDetail();
    logActivity(`Trạng thái phiên chuyển sang <b>${esc(patch.status)}</b>`, '🔔');
  }
});

connection.onreconnecting(() => setConn('● Mất kết nối, đang thử lại...', 'bg-amber-500'));
connection.onreconnected(async () => {
  setConn('● Realtime', 'bg-emerald-500');
  if (state.selectedId) await connection.invoke('JoinAuctionGroup', state.selectedId).catch(() => {});
  await loadAuctions();
  await loadSelected().catch(() => {});
});
connection.onclose(() => {
  setConn('● Ngắt kết nối', 'bg-rose-500');
  setTimeout(startConnection, 5000);
});

async function startConnection() {
  try {
    await connection.start();
    setConn('● Realtime', 'bg-emerald-500');
    if (state.selectedId) await connection.invoke('JoinAuctionGroup', state.selectedId);
  } catch (e) {
    setConn('● Không kết nối được', 'bg-rose-500');
    console.warn('SignalR start failed', e);
    setTimeout(startConnection, 5000);
  }
}

// ---------- Actions ---------------------------------------------------
async function placeBid(amount) {
  const a = state.auctions.get(state.selectedId);
  if (!a) return;
  const errEl = $('bidError');
  errEl.classList.add('hidden');
  $('bidBtn').disabled = true;
  try {
    const res = await api(`/api/auctions/${a.id}/bids`, {
      method: 'POST',
      body: JSON.stringify({ bidderName: myName(), amount: Number(amount) }),
    });
    // Cập nhật ngay từ response (sự kiện SignalR tới sau sẽ được khử trùng theo bidId)
    mergeAuction({ id: a.id, currentPrice: res.currentPrice, minNextBid: res.minNextBid, highestBidder: res.bid.bidderName });
    if (addBidLocal(res.bid)) {
      mergeAuction({ id: a.id, totalBids: Math.max(a.totalBids ?? 0, state.bids.length) });
      renderBids(res.bid.id);
    }
    renderDetail();
    renderList();
    toast(`Đặt giá thành công: ${money(res.bid.amount)}`, 'success');
  } catch (e) {
    errEl.textContent = `❌ [${e.status ?? ''} ${e.code ?? 'ERROR'}] ${e.message}`;
    errEl.classList.remove('hidden');
    toast(e.message, 'error');
  } finally {
    renderDetail();
  }
}

async function endAuction() {
  const a = state.auctions.get(state.selectedId);
  if (!a || !confirm(`Kết thúc phiên "${a.productName}" ngay bây giờ?`)) return;
  try {
    const dto = await api(`/api/auctions/${a.id}/end`, { method: 'POST' });
    state.auctions.set(dto.id, dto);
    renderList();
    renderDetail();
  } catch (e) {
    toast(e.message, 'error');
  }
}

function setQuickDuration(minutes) {
  const now = new Date();
  $('createForm').startTime.value = toLocalInput(now);
  $('createForm').endTime.value = toLocalInput(new Date(now.getTime() + minutes * 60000));
}

async function createAuction(ev) {
  ev.preventDefault();
  const f = ev.target;
  const payload = {
    productName: f.productName.value.trim(),
    description: f.description.value.trim(),
    startingPrice: Number(f.startingPrice.value),
    minStepPrice: Number(f.minStepPrice.value),
    startTime: new Date(f.startTime.value).toISOString(),
    endTime: new Date(f.endTime.value).toISOString(),
  };
  try {
    const dto = await api('/api/auctions', { method: 'POST', body: JSON.stringify(payload) });
    state.auctions.set(dto.id, dto);
    toast(`Đã tạo phiên "${dto.productName}"`, 'success');
    f.productName.value = '';
    f.description.value = '';
    setQuickDuration(5);
    renderList();
    await selectAuction(dto.id);
  } catch (e) {
    toast('Tạo phiên thất bại: ' + e.message, 'error');
  }
}

// ---------- Bootstrap -----------------------------------------------------
function bindUi() {
  $('createForm').addEventListener('submit', createAuction);
  $('toggleCreate').addEventListener('click', () => {
    const hidden = $('createForm').classList.toggle('hidden');
    $('toggleIcon').textContent = hidden ? '▸' : '▾';
  });
  document.querySelectorAll('.quick-duration').forEach((b) =>
    b.addEventListener('click', () => setQuickDuration(Number(b.dataset.duration))));
  $('refreshBtn').addEventListener('click', loadAuctions);

  $('bidForm').addEventListener('submit', (e) => { e.preventDefault(); placeBid($('bidAmount').value); });
  document.querySelectorAll('.quick-bid').forEach((b) => b.addEventListener('click', () => {
    const a = state.auctions.get(state.selectedId);
    if (!a) return;
    $('bidAmount').value = Number(a.minNextBid) + Number(b.dataset.steps) * Number(a.minStepPrice);
  }));
  $('lowBidBtn').addEventListener('click', () => {
    const a = state.auctions.get(state.selectedId);
    if (a) placeBid(Number(a.currentPrice)); // cố ý thấp hơn mức tối thiểu => 400 từ gRPC validation
  });
  $('endBtn').addEventListener('click', endAuction);

  // Đồng hồ đếm ngược
  setInterval(() => {
    state.auctions.forEach((a) => {
      const el = document.querySelector(`[data-countdown="${a.id}"]`);
      if (el) el.textContent = countdownText(a);
    });
    const sel = state.auctions.get(state.selectedId);
    if (sel) $('dCountdown').textContent = countdownText(sel);
  }, 1000);
}

(async function main() {
  initIdentity();
  bindUi();
  setQuickDuration(5);
  await startConnection();
  await loadAuctions();
  const fromHash = location.hash.slice(1);
  if (fromHash && state.auctions.has(fromHash)) await selectAuction(fromHash);
})();
