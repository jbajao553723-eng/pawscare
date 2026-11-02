(() => {
  'use strict';

  const STORAGE_KEY = 'pawcare-crm-v1';
  const SESSION_KEY = 'pawcare-session-v1';
  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const icon = (name) => `<svg aria-hidden="true"><use href="#i-${name}"></use></svg>`;
  const uid = (prefix) => `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  const todayISO = () => new Date().toISOString().slice(0, 10);
  const addDays = (days) => { const d = new Date(); d.setDate(d.getDate() + days); return d.toISOString().slice(0, 10); };
  const prettyDate = (value) => value ? new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(`${value}T08:00:00`)) : '—';
  const prettyShort = (value) => value ? new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric' }).format(new Date(`${value}T08:00:00`)) : '—';
  const escapeHTML = (value = '') => String(value).replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[char]);
  const statusClass = (status = '') => {
    const s = status.toLowerCase();
    if (['confirmed', 'resolved', 'active', 'sent', 'completed', 'approved', 'closed'].some(x => s.includes(x))) return 'success';
    if (['urgent', 'escalated', 'cancelled', 'high'].some(x => s.includes(x))) return 'danger';
    if (['pending', 'follow-up', 'scheduled', 'draft', 'medium'].some(x => s.includes(x))) return 'warning';
    if (['new', 'open', 'in progress', 'low'].some(x => s.includes(x))) return 'info';
    return 'neutral';
  };
  const badge = (value) => `<span class="badge badge--${statusClass(value)}">${escapeHTML(value)}</span>`;

  const seedData = () => ({
    clients: [
      { id: 'CL-1048', name: 'Sofia Reyes', phone: '0917 554 2890', email: 'sofia.reyes@email.com', address: 'Makati City', consent: true, pets: [{ name: 'Milo', species: 'Dog', breed: 'Shih Tzu', sex: 'Male', birthday: '2022-04-18', emoji: '🐶' }], lastVisit: addDays(-12) },
      { id: 'CL-1047', name: 'Gabriel Cruz', phone: '0918 331 9042', email: 'gabriel.c@email.com', address: 'Taguig City', consent: true, pets: [{ name: 'Luna', species: 'Cat', breed: 'Persian', sex: 'Female', birthday: '2021-09-02', emoji: '🐱' }], lastVisit: addDays(-18) },
      { id: 'CL-1046', name: 'Amelia Santos', phone: '0920 842 1175', email: 'amelia.s@email.com', address: 'Pasig City', consent: false, pets: [{ name: 'Coco', species: 'Dog', breed: 'Golden Retriever', sex: 'Female', birthday: '2020-11-11', emoji: '🐕' }], lastVisit: addDays(-26) },
      { id: 'CL-1045', name: 'Daniel Lim', phone: '0916 279 6681', email: 'daniel.lim@email.com', address: 'Quezon City', consent: true, pets: [{ name: 'Pepper', species: 'Rabbit', breed: 'Mini Rex', sex: 'Male', birthday: '2023-01-23', emoji: '🐰' }], lastVisit: addDays(-34) },
      { id: 'CL-1044', name: 'Isabella Tan', phone: '0919 650 4872', email: 'isabella.t@email.com', address: 'Mandaluyong City', consent: true, pets: [{ name: 'Atlas', species: 'Dog', breed: 'Aspin', sex: 'Male', birthday: '2019-06-14', emoji: '🐕' }], lastVisit: addDays(-41) }
    ],
    appointments: [
      { id: 'AP-2088', date: todayISO(), time: '09:00', clientId: 'CL-1048', client: 'Sofia Reyes', pet: 'Milo', emoji: '🐶', service: 'Annual wellness exam', veterinarian: 'Dr. Mia Garcia', status: 'Confirmed', notes: 'First visit after grooming allergy.' },
      { id: 'AP-2089', date: todayISO(), time: '10:30', clientId: 'CL-1047', client: 'Gabriel Cruz', pet: 'Luna', emoji: '🐱', service: 'Core vaccination', veterinarian: 'Dr. Luis Mendoza', status: 'Checked in', notes: '' },
      { id: 'AP-2090', date: todayISO(), time: '13:00', clientId: 'CL-1046', client: 'Amelia Santos', pet: 'Coco', emoji: '🐕', service: 'Dermatology follow-up', veterinarian: 'Dr. Mia Garcia', status: 'Pending', notes: 'Review response to prescribed shampoo.' },
      { id: 'AP-2091', date: todayISO(), time: '15:30', clientId: 'CL-1045', client: 'Daniel Lim', pet: 'Pepper', emoji: '🐰', service: 'Nail trim & wellness', veterinarian: 'Dr. Ava Villanueva', status: 'Confirmed', notes: '' },
      { id: 'AP-2092', date: addDays(1), time: '09:30', clientId: 'CL-1044', client: 'Isabella Tan', pet: 'Atlas', emoji: '🐕', service: 'Senior care assessment', veterinarian: 'Dr. Luis Mendoza', status: 'Scheduled', notes: '' }
    ],
    inquiries: [
      { id: 'INQ-3021', created: addDays(-1), client: 'Sofia Reyes', contact: '0917 554 2890', pet: 'Milo', channel: 'Phone', category: 'Post-visit care', subject: 'Mild redness after grooming', details: 'Asked whether mild skin redness needs an earlier follow-up.', priority: 'Medium', status: 'Follow-up', preferred: 'SMS', consent: true, owner: 'Alex Morgan', followUp: todayISO() },
      { id: 'INQ-3020', created: addDays(-1), client: 'New lead — Rafael Ong', contact: 'rafael.ong@email.com', pet: 'Unnamed puppy', channel: 'Website', category: 'New client', subject: 'First puppy wellness package', details: 'Comparing vaccination and wellness package inclusions.', priority: 'Low', status: 'New', preferred: 'Email', consent: true, owner: 'Alex Morgan', followUp: addDays(1) },
      { id: 'INQ-3019', created: addDays(-2), client: 'Gabriel Cruz', contact: '0918 331 9042', pet: 'Luna', channel: 'Messenger', category: 'Appointment', subject: 'Request to move vaccination schedule', details: 'Client needs an afternoon slot this week.', priority: 'Medium', status: 'Resolved', preferred: 'Messenger', consent: true, owner: 'Alex Morgan', followUp: '' },
      { id: 'INQ-3018', created: addDays(-3), client: 'Amelia Santos', contact: '0920 842 1175', pet: 'Coco', channel: 'Walk-in', category: 'Billing', subject: 'Clarify dermatology package price', details: 'Requested detailed cost breakdown before next visit.', priority: 'High', status: 'Open', preferred: 'Phone', consent: false, owner: 'Alex Morgan', followUp: todayISO() }
    ],
    reminders: [
      { id: 'REM-810', client: 'Sofia Reyes', pet: 'Milo', type: 'Post-visit check-in', channel: 'SMS', due: todayISO(), status: 'Pending', message: 'Hi Sofia, how is Milo feeling after the visit?' },
      { id: 'REM-811', client: 'Gabriel Cruz', pet: 'Luna', type: 'Vaccination reminder', channel: 'Email', due: addDays(2), status: 'Scheduled', message: 'Luna’s vaccination appointment is coming up.' },
      { id: 'REM-812', client: 'Isabella Tan', pet: 'Atlas', type: 'Appointment reminder', channel: 'SMS', due: addDays(1), status: 'Scheduled', message: 'A friendly reminder for Atlas’s senior care assessment.' }
    ],
    promotions: [
      { id: 'PRO-110', name: 'Golden Years Wellness', code: 'SENIOR20', type: '20% off', audience: 'Senior pets (7+ years)', channel: 'SMS + Email', start: addDays(-5), end: addDays(25), status: 'Active', approval: 'Approved', goal: 'Increase senior wellness assessments and early screening uptake.', reach: 184, claims: 27, conversion: 15, createdBy: 'Alex Morgan' },
      { id: 'PRO-109', name: 'New Puppy Welcome', code: 'HELLOPUP', type: '₱500 off', audience: 'New puppy leads', channel: 'Email', start: addDays(3), end: addDays(35), status: 'Scheduled', approval: 'Approved', goal: 'Convert first-time puppy inquiries into wellness appointments.', reach: 76, claims: 12, conversion: 16, createdBy: 'Alex Morgan' },
      { id: 'PRO-108', name: 'Dental Care Month', code: 'SMILE15', type: '15% off', audience: 'Dogs & cats due for dental care', channel: 'SMS', start: addDays(10), end: addDays(40), status: 'Draft', approval: 'Needs review', goal: 'Re-engage clients with overdue dental assessments.', reach: 0, claims: 0, conversion: 0, createdBy: 'Alex Morgan' }
    ],
    feedback: [
      { id: 'FB-512', date: addDays(-1), client: 'Sofia Reyes', pet: 'Milo', rating: 5, category: 'Service', comment: 'The receptionist explained every next step clearly.', status: 'Reviewed', escalation: 'None' },
      { id: 'FB-511', date: addDays(-2), client: 'Amelia Santos', pet: 'Coco', rating: 3, category: 'Wait time', comment: 'Coco was seen later than our appointment time.', status: 'Open', escalation: 'Manager review' },
      { id: 'FB-510', date: addDays(-4), client: 'Daniel Lim', pet: 'Pepper', rating: 5, category: 'Veterinarian', comment: 'Very gentle handling and helpful care advice.', status: 'Reviewed', escalation: 'None' },
      { id: 'FB-509', date: addDays(-6), client: 'Isabella Tan', pet: 'Atlas', rating: 2, category: 'Billing', comment: 'The final invoice was not the amount I expected.', status: 'Escalated', escalation: 'Urgent' }
    ],
    activities: [
      { type: 'message', text: 'Inquiry logged for Milo', detail: 'Post-visit care · Phone', time: '18 min ago' },
      { type: 'calendar', text: 'Luna checked in', detail: 'Core vaccination · Dr. Mendoza', time: '34 min ago' },
      { type: 'tag', text: 'Promotion claim recorded', detail: 'Golden Years Wellness', time: '1 hr ago' },
      { type: 'users', text: 'Client profile updated', detail: 'Sofia Reyes · Contact consent', time: '2 hrs ago' }
    ]
  });

  let db = loadData();
  let session = loadSession();
  let activePage = 'dashboard';
  let loginRole = 'receptionist';

  const users = {
    receptionist: { role: 'receptionist', name: 'Alex Morgan', initials: 'AM', email: 'reception@pawcare.test', password: 'pawcare123', title: 'Receptionist' },
    manager: { role: 'manager', name: 'Jordan Lee', initials: 'JL', email: 'manager@pawcare.test', password: 'pawcare123', title: 'Clinic Manager' }
  };

  const nav = {
    receptionist: [
      { page: 'dashboard', label: 'Dashboard', icon: 'grid' },
      { label: 'Client care', heading: true },
      { page: 'clients', label: 'Client profiles', icon: 'users' },
      { page: 'appointments', label: 'Appointments', icon: 'calendar' },
      { page: 'inquiries', label: 'Inquiries', icon: 'message', count: () => db.inquiries.filter(x => !['Resolved', 'Closed'].includes(x.status)).length },
      { page: 'reminders', label: 'Reminders & follow-ups', icon: 'bell' },
      { page: 'promotions', label: 'Promotional offers', icon: 'tag' }
    ],
    manager: [
      { page: 'dashboard', label: 'Dashboard', icon: 'grid' },
      { label: 'Operations', heading: true },
      { page: 'appointments', label: 'Customer appointments', icon: 'calendar' },
      { page: 'satisfaction', label: 'Client satisfaction', icon: 'heart' },
      { page: 'feedback', label: 'Feedback & escalations', icon: 'alert', count: () => db.feedback.filter(x => ['Open', 'Escalated'].includes(x.status)).length },
      { page: 'reports', label: 'Reports', icon: 'chart' }
    ]
  };

  function loadData() {
    try { return JSON.parse(localStorage.getItem(STORAGE_KEY)) || seedData(); }
    catch { return seedData(); }
  }
  function saveData() { localStorage.setItem(STORAGE_KEY, JSON.stringify(db)); }
  function loadSession() {
    try { return JSON.parse(sessionStorage.getItem(SESSION_KEY)); }
    catch { return null; }
  }
  function saveSession(value) {
    session = value;
    value ? sessionStorage.setItem(SESSION_KEY, JSON.stringify(value)) : sessionStorage.removeItem(SESSION_KEY);
  }
  function toast(message) {
    const node = document.createElement('div');
    node.className = 'toast';
    node.innerHTML = `${icon('check')}<span>${escapeHTML(message)}</span>`;
    $('#toast-root').append(node);
    setTimeout(() => node.remove(), 3200);
  }
  function recordActivity(type, text, detail) {
    db.activities.unshift({ type, text, detail, time: 'Just now' });
    db.activities = db.activities.slice(0, 12);
  }

  function setLoginRole(role) {
    loginRole = role;
    $$('.role-option').forEach(btn => {
      const selected = btn.dataset.loginRole === role;
      btn.classList.toggle('is-selected', selected);
      btn.setAttribute('aria-checked', String(selected));
    });
    $('#login-email').value = users[role].email;
    $('#login-error').textContent = '';
  }

  function initLogin() {
    $$('.role-option').forEach(btn => btn.addEventListener('click', () => setLoginRole(btn.dataset.loginRole)));
    $('#toggle-password').addEventListener('click', () => {
      const input = $('#login-password');
      const show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      $('#toggle-password').textContent = show ? 'Hide' : 'Show';
    });
    $('#login-form').addEventListener('submit', (event) => {
      event.preventDefault();
      const user = users[loginRole];
      const email = $('#login-email').value.trim().toLowerCase();
      const password = $('#login-password').value;
      if (email !== user.email || password !== user.password) {
        $('#login-error').textContent = `Use the ${user.title.toLowerCase()} demo email and password shown below.`;
        return;
      }
      saveSession({ role: user.role, name: user.name });
      activePage = 'dashboard';
      showApp();
      toast(`Welcome back, ${user.name.split(' ')[0]}`);
    });
  }

  function showApp() {
    const user = users[session.role];
    $('#login-view').hidden = true;
    $('#app-view').hidden = false;
    $('#side-name').textContent = user.name;
    $('#side-role').textContent = user.title;
    $('#side-avatar').textContent = user.initials;
    $('.profile-chip .avatar').textContent = user.initials;
    $('.profile-chip strong').textContent = user.name;
    $('.profile-chip small').textContent = user.title;
    renderNav();
    navigate(activePage);
  }

  function renderNav() {
    $('#main-nav').innerHTML = nav[session.role].map(item => item.heading
      ? `<span class="nav-label">${item.label}</span>`
      : `<button class="nav-item ${item.page === activePage ? 'is-active' : ''}" data-page="${item.page}">${icon(item.icon)}<span>${item.label}</span>${item.count ? `<span class="nav-count">${item.count()}</span>` : ''}</button>`
    ).join('');
    $$('.nav-item').forEach(btn => btn.addEventListener('click', () => navigate(btn.dataset.page)));
  }

  const pageMeta = {
    dashboard: ['Overview', 'Dashboard'], clients: ['Client care', 'Client profiles'], appointments: ['Operations', 'Appointments'],
    inquiries: ['Client care', 'Inquiries'], reminders: ['Client care', 'Reminders & follow-ups'], promotions: ['Engagement', 'Promotional offers'],
    satisfaction: ['Experience', 'Client satisfaction'], feedback: ['Experience', 'Feedback & escalations'], reports: ['Insights', 'Reports']
  };

  function navigate(page) {
    activePage = page;
    const user = users[session.role];
    const meta = pageMeta[page] || ['PawCare', page];
    $('#breadcrumb').textContent = `PawCare / ${meta[0]}`;
    $('#page-title').textContent = page === 'dashboard' ? `${greeting()}, ${user.name.split(' ')[0]}` : meta[1];
    renderNav();
    const renderers = { dashboard: renderDashboard, clients: renderClients, appointments: renderAppointments, inquiries: renderInquiries, reminders: renderReminders, promotions: renderPromotions, satisfaction: renderSatisfaction, feedback: renderFeedback, reports: renderReports };
    $('#content').innerHTML = renderers[page] ? renderers[page]() : '';
    bindPageActions(page);
    $('#sidebar').classList.remove('is-open');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function greeting() {
    const hour = new Date().getHours();
    return hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
  }

  function metric(iconName, value, label, trend = '', color = '') {
    return `<article class="metric-card"><div class="metric-top"><span class="metric-icon ${color}">${icon(iconName)}</span><span class="metric-trend">${trend}</span></div><h3>${value}</h3><p>${label}</p></article>`;
  }
  function contentHead(title, subtitle, actions = '') {
    return `<div class="content-head"><div><h2>${title}</h2><p>${subtitle}</p></div><div class="head-actions">${actions}</div></div>`;
  }
  function appointmentRows(items) {
    return items.map(a => `<div class="appointment-row"><div class="time-block">${formatTime(a.time)}<span>${a.date === todayISO() ? 'Today' : prettyShort(a.date)}</span></div><div class="pet-info"><span class="pet-avatar">${a.emoji || '🐾'}</span><div><strong>${escapeHTML(a.pet)} · ${escapeHTML(a.client)}</strong><span>${escapeHTML(a.service)} · ${escapeHTML(a.veterinarian)}</span></div></div>${badge(a.status)}</div>`).join('');
  }
  function formatTime(value) {
    const [h, m] = value.split(':').map(Number);
    return `${h % 12 || 12}:${String(m).padStart(2, '0')} ${h >= 12 ? 'PM' : 'AM'}`;
  }

  function renderDashboard() {
    const role = session.role;
    const todayAppointments = db.appointments.filter(x => x.date === todayISO());
    const openInquiries = db.inquiries.filter(x => !['Resolved', 'Closed'].includes(x.status));
    const pendingReminders = db.reminders.filter(x => x.status !== 'Sent');
    const openFeedback = db.feedback.filter(x => ['Open', 'Escalated'].includes(x.status));
    const activities = db.activities.slice(0, 4).map(a => `<div class="activity-item"><span class="activity-dot">${icon(a.type)}</span><div><strong>${escapeHTML(a.text)}</strong><p>${escapeHTML(a.detail)}</p><time>${escapeHTML(a.time)}</time></div></div>`).join('');
    if (role === 'receptionist') {
      return `${contentHead('Front desk overview', 'A clear view of today’s care coordination and client conversations.', `<span class="date-chip">${new Intl.DateTimeFormat('en-PH', { weekday: 'long', month: 'long', day: 'numeric' }).format(new Date())}</span><button class="btn btn--primary" data-action="new-appointment">${icon('plus')} Book appointment</button>`)}
        <div class="metric-grid">${metric('calendar', todayAppointments.length, 'Appointments today', '+2 from yesterday')}${metric('message', openInquiries.length, 'Open inquiries', '2 due today', 'blue')}${metric('bell', pendingReminders.length, 'Follow-ups queued', 'On track', 'gold')}${metric('users', db.clients.length, 'Active client profiles', '+3 this month')}</div>
        <div class="dashboard-grid"><div class="stack"><section class="panel"><div class="panel-head"><div><h3>Today’s appointments</h3><p>${todayAppointments.length} visits across the care team</p></div><button class="text-btn" data-page-link="appointments">View schedule →</button></div><div class="appointment-list">${appointmentRows(todayAppointments)}</div></section><section class="panel"><div class="panel-head"><div><h3>Quick actions</h3><p>Common front desk workflows</p></div></div><div class="quick-actions"><button class="quick-action" data-action="new-client">${icon('users')}<strong>Add client & pet</strong><span>Create a complete care profile</span></button><button class="quick-action" data-action="new-inquiry">${icon('message')}<strong>Log inquiry</strong><span>Capture a client conversation</span></button><button class="quick-action" data-action="new-reminder">${icon('bell')}<strong>Schedule follow-up</strong><span>Keep care on track</span></button><button class="quick-action" data-action="new-promotion">${icon('tag')}<strong>Create offer</strong><span>Build a targeted campaign</span></button></div></section></div><div class="stack"><section class="panel"><div class="panel-head"><div><h3>Recent activity</h3><p>Latest updates across the clinic</p></div></div><div class="activity-list">${activities}</div></section><section class="panel"><div class="panel-head"><div><h3>Client care pulse</h3><p>Today’s follow-through</p></div></div><div class="progress-row"><div class="progress-meta"><span>Inquiries answered</span><span>78%</span></div><div class="progress"><i style="width:78%"></i></div></div><div class="progress-row"><div class="progress-meta"><span>Reminders sent</span><span>64%</span></div><div class="progress gold"><i style="width:64%"></i></div></div><div class="progress-row"><div class="progress-meta"><span>Profiles complete</span><span>91%</span></div><div class="progress"><i style="width:91%"></i></div></div></section><button class="text-btn" data-action="reset-data">Reset demo data</button></div></div>`;
    }
    const avg = (db.feedback.reduce((sum, x) => sum + Number(x.rating), 0) / db.feedback.length).toFixed(1);
    return `${contentHead('Operations overview', 'Monitor client experience, appointment flow, and team follow-through.', `<span class="date-chip">${new Intl.DateTimeFormat('en-PH', { weekday: 'long', month: 'long', day: 'numeric' }).format(new Date())}</span><button class="btn btn--primary" data-page-link="reports">${icon('chart')} Open reports</button>`)}
      <div class="metric-grid">${metric('calendar', db.appointments.filter(x => x.date >= todayISO()).length, 'Upcoming appointments', '+8% this week')}${metric('heart', `${avg}/5`, 'Client satisfaction', '+0.2 this month', 'gold')}${metric('alert', openFeedback.length, 'Open escalations', openFeedback.length ? 'Needs attention' : 'All clear', 'red')}${metric('tag', db.promotions.filter(x => x.status === 'Active').length, 'Active campaigns', '15% conversion', 'blue')}</div>
      <div class="dashboard-grid"><div class="stack"><section class="panel"><div class="panel-head"><div><h3>Appointments this week</h3><p>Daily scheduled visit volume</p></div><button class="text-btn" data-page-link="appointments">Manage appointments →</button></div>${barChart([12, 16, 14, 19, 17, 9, 6], ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'])}</section><section class="panel"><div class="panel-head"><div><h3>Today’s visit flow</h3><p>${todayAppointments.length} customer appointments</p></div></div><div class="appointment-list">${appointmentRows(todayAppointments)}</div></section></div><div class="stack"><section class="panel"><div class="panel-head"><div><h3>Experience signals</h3><p>What needs manager attention</p></div></div><div class="activity-list">${db.feedback.filter(x => ['Open','Escalated'].includes(x.status)).map(f => `<div class="activity-item"><span class="activity-dot">${icon('alert')}</span><div><strong>${escapeHTML(f.category)} · ${escapeHTML(f.client)}</strong><p>${escapeHTML(f.comment)}</p>${badge(f.status)}</div></div>`).join('') || '<p class="muted">No open feedback.</p>'}</div></section><section class="panel"><div class="panel-head"><div><h3>Team targets</h3><p>Current month progress</p></div></div><div class="progress-row"><div class="progress-meta"><span>Appointment utilization</span><span>84%</span></div><div class="progress"><i style="width:84%"></i></div></div><div class="progress-row"><div class="progress-meta"><span>Inquiry resolution SLA</span><span>88%</span></div><div class="progress gold"><i style="width:88%"></i></div></div><div class="progress-row"><div class="progress-meta"><span>Client satisfaction goal</span><span>92%</span></div><div class="progress"><i style="width:92%"></i></div></div></section><button class="text-btn" data-action="reset-data">Reset demo data</button></div></div>`;
  }

  function barChart(values, labels) {
    const max = Math.max(...values);
    return `<div class="chart-wrap">${values.map((v, i) => `<div class="chart-bar" style="height:${Math.max(12, v / max * 100)}%"><i>${v}</i><span>${labels[i]}</span></div>`).join('')}</div>`;
  }

  function renderClients() {
    return `${contentHead('Client profiles', 'Keep owner contact details and patient information connected.', `<button class="btn btn--primary" data-action="new-client">${icon('plus')} Add client</button>`)}
      ${tableToolbar('Search by client, pet, phone, or email…', '', '')}
      <div class="data-panel"><table class="data-table"><thead><tr><th>Client & patient</th><th>Contact</th><th>Location</th><th>Last visit</th><th>Marketing consent</th><th></th></tr></thead><tbody>${db.clients.map(c => { const p = c.pets[0]; return `<tr data-search-row="${escapeHTML(`${c.name} ${p.name} ${c.phone} ${c.email}`.toLowerCase())}"><td><div class="table-primary"><span class="pet-avatar">${p.emoji}</span><div><strong>${escapeHTML(c.name)}</strong><span>${escapeHTML(p.name)} · ${escapeHTML(p.breed)}</span></div></div></td><td>${escapeHTML(c.phone)}<br><span class="muted">${escapeHTML(c.email)}</span></td><td>${escapeHTML(c.address)}</td><td>${prettyDate(c.lastVisit)}</td><td>${c.consent ? badge('Opted in') : badge('No consent')}</td><td><button class="table-action" data-view-client="${c.id}">View profile</button></td></tr>`; }).join('')}</tbody></table></div>`;
  }

  function tableToolbar(placeholder, filterOptions, extra) {
    return `<div class="toolbar"><label class="table-search">${icon('search')}<input data-table-search type="search" placeholder="${placeholder}"></label>${filterOptions || ''}${extra || ''}</div>`;
  }

  function renderAppointments() {
    const canCreate = session.role === 'receptionist';
    return `${contentHead(session.role === 'manager' ? 'Customer appointments' : 'Appointments', session.role === 'manager' ? 'Monitor appointment volume, visit status, and service demand.' : 'Book visits and keep every appointment detail accurate.', canCreate ? `<button class="btn btn--primary" data-action="new-appointment">${icon('plus')} Book appointment</button>` : `<button class="btn btn--outline" data-page-link="reports">${icon('chart')} View report</button>`)}
      ${tableToolbar('Search client, pet, service, or vet…', `<select data-table-filter><option value="">All statuses</option><option>Confirmed</option><option>Checked in</option><option>Pending</option><option>Scheduled</option><option>Completed</option></select>`, '')}
      <div class="data-panel"><table class="data-table"><thead><tr><th>Date & time</th><th>Client & patient</th><th>Service</th><th>Veterinarian</th><th>Status</th><th></th></tr></thead><tbody>${db.appointments.slice().sort((a,b) => `${a.date}${a.time}`.localeCompare(`${b.date}${b.time}`)).map(a => `<tr data-search-row="${escapeHTML(`${a.client} ${a.pet} ${a.service} ${a.veterinarian}`.toLowerCase())}" data-filter-value="${a.status}"><td><strong>${prettyShort(a.date)}</strong><br><span class="muted">${formatTime(a.time)}</span></td><td><div class="table-primary"><span class="pet-avatar">${a.emoji}</span><div><strong>${escapeHTML(a.pet)}</strong><span>${escapeHTML(a.client)}</span></div></div></td><td>${escapeHTML(a.service)}</td><td>${escapeHTML(a.veterinarian)}</td><td>${badge(a.status)}</td><td><button class="table-action" data-update-appointment="${a.id}">${session.role === 'manager' ? 'Review' : 'Update'}</button></td></tr>`).join('')}</tbody></table></div>`;
  }

  function renderInquiries() {
    const open = db.inquiries.filter(x => !['Resolved', 'Closed'].includes(x.status)).length;
    const due = db.inquiries.filter(x => x.followUp === todayISO() && !['Resolved', 'Closed'].includes(x.status)).length;
    const leads = db.inquiries.filter(x => x.category === 'New client').length;
    return `${contentHead('Client inquiries', 'Capture every conversation as structured CRM data and make follow-through visible.', `<button class="btn btn--primary" data-action="new-inquiry">${icon('plus')} Log inquiry</button>`)}
      <div class="metric-grid">${metric('message', open, 'Open conversations', 'Live workload')}${metric('bell', due, 'Follow-ups due today', due ? 'Action needed' : 'All clear', 'gold')}${metric('users', leads, 'New client leads', '+1 this week', 'blue')}${metric('check', `${Math.round((db.inquiries.length-open)/db.inquiries.length*100)}%`, 'Resolution rate', 'Last 30 days')}</div>
      ${tableToolbar('Search inquiry, client, pet, contact, or subject…', `<select data-table-filter><option value="">All statuses</option><option>New</option><option>Open</option><option>Follow-up</option><option>Resolved</option></select>`, `<button class="btn btn--outline" data-export="inquiries">Export data</button>`)}
      <div class="data-panel"><table class="data-table"><thead><tr><th>Inquiry</th><th>Client & contact</th><th>Source</th><th>Priority</th><th>Follow-up</th><th>Status</th><th></th></tr></thead><tbody>${db.inquiries.map(q => `<tr data-search-row="${escapeHTML(`${q.id} ${q.client} ${q.pet} ${q.contact} ${q.subject} ${q.category}`.toLowerCase())}" data-filter-value="${q.status}"><td><strong>${escapeHTML(q.subject)}</strong><br><span class="muted">${q.id} · ${escapeHTML(q.category)}</span></td><td>${escapeHTML(q.client)}<br><span class="muted">${escapeHTML(q.contact)} · ${escapeHTML(q.pet)}</span></td><td>${escapeHTML(q.channel)}<br><span class="muted">Prefers ${escapeHTML(q.preferred)}</span></td><td>${badge(q.priority)}</td><td>${prettyShort(q.followUp)}</td><td>${badge(q.status)}</td><td><button class="table-action" data-view-inquiry="${q.id}">Open</button></td></tr>`).join('')}</tbody></table></div>`;
  }

  function renderReminders() {
    return `${contentHead('Reminders & follow-ups', 'Schedule thoughtful touchpoints across the client care journey.', `<button class="btn btn--primary" data-action="new-reminder">${icon('plus')} New reminder</button>`)}
      ${tableToolbar('Search client, pet, or reminder type…', `<select data-table-filter><option value="">All statuses</option><option>Pending</option><option>Scheduled</option><option>Sent</option></select>`, '')}
      <div class="cards-grid">${db.reminders.map(r => `<article class="record-card" data-search-row="${escapeHTML(`${r.client} ${r.pet} ${r.type}`.toLowerCase())}" data-filter-value="${r.status}"><div class="record-card-top"><div><h3>${escapeHTML(r.type)}</h3><p class="meta">${escapeHTML(r.client)} · ${escapeHTML(r.pet)}</p></div>${badge(r.status)}</div><dl><div><dt>Due</dt><dd>${prettyDate(r.due)}</dd></div><div><dt>Channel</dt><dd>${escapeHTML(r.channel)}</dd></div></dl><p class="card-note">“${escapeHTML(r.message)}”</p><div class="card-actions">${r.status !== 'Sent' ? `<button class="btn btn--soft" data-send-reminder="${r.id}">Mark sent</button>` : ''}<button class="btn btn--outline" data-edit-reminder="${r.id}">Edit</button></div></article>`).join('')}</div>`;
  }

  function renderPromotions() {
    const claims = db.promotions.reduce((sum, x) => sum + Number(x.claims), 0);
    return `${contentHead('Promotional offers', 'Build consent-aware campaigns from inquiry and client data.', `<button class="btn btn--primary" data-action="new-promotion">${icon('plus')} Create offer</button>`)}
      <div class="metric-grid">${metric('tag', db.promotions.filter(x => x.status === 'Active').length, 'Active offers', 'Current campaigns')}${metric('users', db.promotions.reduce((s,x)=>s+Number(x.reach),0), 'Clients reached', 'Consent-filtered', 'blue')}${metric('check', claims, 'Offer claims', '+12% this month', 'gold')}${metric('chart', `${Math.round(db.promotions.reduce((s,x)=>s+Number(x.conversion),0)/db.promotions.length)}%`, 'Avg. conversion', 'Across campaigns')}</div>
      ${tableToolbar('Search campaign, code, or audience…', `<select data-table-filter><option value="">All statuses</option><option>Active</option><option>Scheduled</option><option>Draft</option><option>Completed</option></select>`, `<button class="btn btn--outline" data-export="promotions">Export data</button>`)}
      <div class="cards-grid">${db.promotions.map(p => `<article class="record-card promo-card" data-search-row="${escapeHTML(`${p.name} ${p.code} ${p.audience}`.toLowerCase())}" data-filter-value="${p.status}"><div class="promo-ribbon"></div><div class="record-card-top"><div><h3>${escapeHTML(p.name)}</h3><p class="meta">${escapeHTML(p.audience)}</p></div>${badge(p.status)}</div><div class="promo-code">${escapeHTML(p.code)}</div><p class="meta">${escapeHTML(p.type)} · ${escapeHTML(p.channel)}</p><p class="goal">${escapeHTML(p.goal)}</p><div class="promo-stats"><div><strong>${p.reach}</strong><span>Reached</span></div><div><strong>${p.claims}</strong><span>Claims</span></div><div><strong>${p.conversion}%</strong><span>Conversion</span></div></div><div class="card-actions"><button class="btn btn--soft" data-edit-promotion="${p.id}">Manage</button>${p.approval !== 'Approved' ? `<span class="badge badge--warning">${escapeHTML(p.approval)}</span>` : `<span class="badge badge--success">Approved</span>`}</div></article>`).join('')}</div>`;
  }

  function renderSatisfaction() {
    const avg = db.feedback.reduce((sum,x)=>sum+Number(x.rating),0)/db.feedback.length;
    const counts = [5,4,3,2,1].map(n => db.feedback.filter(x => Number(x.rating) === n).length);
    const max = Math.max(...counts, 1);
    return `${contentHead('Client satisfaction', 'Monitor experience signals across every patient and client touchpoint.', `<button class="btn btn--outline" data-export="feedback">Export feedback</button>`)}
      <section class="satisfaction-hero"><div class="score-ring"><div><strong>${avg.toFixed(1)}</strong><span>out of 5</span></div></div><div><h3>Clients feel well cared for</h3><p>Most feedback highlights clear communication and gentle patient handling. Wait time and billing clarity are the main improvement opportunities.</p><div class="rating-bars">${[5,4,3,2,1].map((n,i)=>`<div class="rating-line"><span>${n} stars</span><div class="progress"><i style="width:${counts[i]/max*100}%"></i></div><strong>${counts[i]}</strong></div>`).join('')}</div></div></section>
      <div class="metric-grid">${metric('heart', `${Math.round(avg/5*100)}%`, 'Satisfaction index', '+4% this month', 'gold')}${metric('message', db.feedback.length, 'Responses collected', 'Recent sample')}${metric('alert', db.feedback.filter(x=>x.rating<=3).length, 'Detractor signals', 'Review recommended', 'red')}${metric('check', db.feedback.filter(x=>x.status==='Reviewed').length, 'Feedback reviewed', 'Team follow-through', 'blue')}</div>
      <section class="panel"><div class="panel-head"><div><h3>Experience themes</h3><p>Feedback grouped by client journey area</p></div></div>${barChart([92,88,79,84,90],['Care','Clarity','Wait','Billing','Follow-up'])}</section>`;
  }

  function renderFeedback() {
    return `${contentHead('Feedback & escalations', 'Review client concerns, assign follow-through, and close the loop.', `<button class="btn btn--primary" data-action="new-feedback">${icon('plus')} Record feedback</button>`)}
      ${tableToolbar('Search client, pet, category, or comment…', `<select data-table-filter><option value="">All statuses</option><option>Open</option><option>Escalated</option><option>Reviewed</option><option>Closed</option></select>`, '')}
      <div class="data-panel"><table class="data-table"><thead><tr><th>Date</th><th>Client & patient</th><th>Rating</th><th>Category</th><th>Feedback</th><th>Status</th><th></th></tr></thead><tbody>${db.feedback.map(f=>`<tr data-search-row="${escapeHTML(`${f.client} ${f.pet} ${f.category} ${f.comment}`.toLowerCase())}" data-filter-value="${f.status}"><td>${prettyShort(f.date)}</td><td><strong>${escapeHTML(f.client)}</strong><br><span class="muted">${escapeHTML(f.pet)}</span></td><td><span style="color:#a66d20">${'★'.repeat(f.rating)}${'☆'.repeat(5-f.rating)}</span></td><td>${escapeHTML(f.category)}<br><span class="muted">${escapeHTML(f.escalation)}</span></td><td>${escapeHTML(f.comment)}</td><td>${badge(f.status)}</td><td><button class="table-action" data-review-feedback="${f.id}">Review</button></td></tr>`).join('')}</tbody></table></div>`;
  }

  function renderReports() {
    const open = db.inquiries.filter(x=>!['Resolved','Closed'].includes(x.status)).length;
    const resolved = db.inquiries.length-open;
    const categoryCounts = ['Appointment','Post-visit care','New client','Billing'].map(c=>db.inquiries.filter(x=>x.category===c).length);
    const topPromos = db.promotions.slice().sort((a,b)=>b.conversion-a.conversion);
    return `${contentHead('Reports', 'Turn clinic activity and CRM data into practical decisions.', `<button class="btn btn--outline" data-export="all">Export all data</button><button class="btn btn--primary" onclick="window.print()">Print report</button>`)}
      <div class="metric-grid">${metric('calendar', db.appointments.length, 'Appointments tracked', 'Current dataset')}${metric('message', `${resolved}/${db.inquiries.length}`, 'Inquiries resolved', `${open} still open`, 'blue')}${metric('heart', `${(db.feedback.reduce((s,x)=>s+x.rating,0)/db.feedback.length).toFixed(1)}/5`, 'Average satisfaction', 'Experience score', 'gold')}${metric('tag', db.promotions.reduce((s,x)=>s+x.claims,0), 'Promotion claims', 'Campaign impact')}</div>
      <div class="dashboard-grid"><div class="stack"><section class="panel"><div class="panel-head"><div><h3>Appointment demand</h3><p>Visit volume by day</p></div></div>${barChart([12,16,14,19,17,9,6],['Mon','Tue','Wed','Thu','Fri','Sat','Sun'])}</section><section class="panel"><div class="panel-head"><div><h3>Inquiry mix</h3><p>What clients contact the clinic about</p></div></div>${barChart(categoryCounts,['Booking','Aftercare','New','Billing'])}</section></div><div class="stack"><section class="panel"><div class="panel-head"><div><h3>Campaign performance</h3><p>Ranked by conversion</p></div></div>${topPromos.map(p=>`<div class="progress-row"><div class="progress-meta"><span>${escapeHTML(p.name)}</span><span>${p.conversion}%</span></div><div class="progress ${p.conversion<10?'gold':''}"><i style="width:${Math.min(100,p.conversion*4)}%"></i></div></div>`).join('')}</section><section class="panel"><div class="panel-head"><div><h3>Data quality</h3><p>CRM collection completeness</p></div></div><div class="progress-row"><div class="progress-meta"><span>Contact information</span><span>96%</span></div><div class="progress"><i style="width:96%"></i></div></div><div class="progress-row"><div class="progress-meta"><span>Communication consent</span><span>82%</span></div><div class="progress gold"><i style="width:82%"></i></div></div><div class="progress-row"><div class="progress-meta"><span>Inquiry source captured</span><span>100%</span></div><div class="progress"><i style="width:100%"></i></div></div></section></div></div>`;
  }

  function bindPageActions(page) {
    $$('[data-page-link]').forEach(btn => btn.addEventListener('click', () => navigate(btn.dataset.pageLink)));
    $$('[data-action]').forEach(btn => btn.addEventListener('click', () => handleAction(btn.dataset.action)));
    const search = $('[data-table-search]');
    const filter = $('[data-table-filter]');
    const applyFilters = () => {
      const term = search?.value.trim().toLowerCase() || '';
      const status = filter?.value || '';
      $$('[data-search-row]').forEach(row => {
        const matchTerm = !term || row.dataset.searchRow.includes(term);
        const matchStatus = !status || row.dataset.filterValue === status;
        row.style.display = matchTerm && matchStatus ? '' : 'none';
      });
    };
    search?.addEventListener('input', applyFilters);
    filter?.addEventListener('change', applyFilters);
    $$('[data-view-client]').forEach(btn => btn.addEventListener('click', () => openClient(btn.dataset.viewClient)));
    $$('[data-update-appointment]').forEach(btn => btn.addEventListener('click', () => openAppointment(btn.dataset.updateAppointment)));
    $$('[data-view-inquiry]').forEach(btn => btn.addEventListener('click', () => openInquiry(btn.dataset.viewInquiry)));
    $$('[data-send-reminder]').forEach(btn => btn.addEventListener('click', () => { const r=db.reminders.find(x=>x.id===btn.dataset.sendReminder); r.status='Sent'; recordActivity('bell','Reminder sent',`${r.type} · ${r.client}`); saveData(); navigate('reminders'); toast('Reminder marked as sent'); }));
    $$('[data-edit-reminder]').forEach(btn => btn.addEventListener('click', () => openReminder(btn.dataset.editReminder)));
    $$('[data-edit-promotion]').forEach(btn => btn.addEventListener('click', () => openPromotion(btn.dataset.editPromotion)));
    $$('[data-review-feedback]').forEach(btn => btn.addEventListener('click', () => openFeedback(btn.dataset.reviewFeedback)));
    $$('[data-export]').forEach(btn => btn.addEventListener('click', () => exportData(btn.dataset.export)));
  }

  function handleAction(action) {
    const map = { 'new-client': () => openClient(), 'new-appointment': () => openAppointment(), 'new-inquiry': () => openInquiry(), 'new-reminder': () => openReminder(), 'new-promotion': () => openPromotion(), 'new-feedback': () => openFeedback() };
    if (map[action]) map[action]();
    if (action === 'reset-data' && confirm('Reset all locally stored demo records to their original state?')) { db=seedData(); saveData(); navigate('dashboard'); toast('Demo data reset'); }
  }

  function openModal(title, subtitle, body, onSubmit, wide = false) {
    $('#modal-root').innerHTML = `<div class="modal-backdrop"><section class="modal ${wide ? 'modal--wide' : ''}" role="dialog" aria-modal="true" aria-labelledby="modal-title"><div class="modal-head"><div><h2 id="modal-title">${title}</h2><p>${subtitle}</p></div><button type="button" class="icon-btn" data-close-modal aria-label="Close">${icon('x')}</button></div><form id="modal-form"><div class="modal-body">${body}<div class="modal-actions"><button type="button" class="btn btn--outline" data-close-modal>Cancel</button><button type="submit" class="btn btn--primary">Save changes</button></div></div></form></section></div>`;
    const close = () => $('#modal-root').innerHTML = '';
    $$('[data-close-modal]').forEach(btn => btn.addEventListener('click', close));
    $('.modal-backdrop').addEventListener('click', e => { if (e.target === e.currentTarget) close(); });
    $('#modal-form').addEventListener('submit', e => { e.preventDefault(); onSubmit(new FormData(e.currentTarget)); close(); });
    setTimeout(() => $('.modal input, .modal select, .modal textarea')?.focus(), 30);
  }

  function openClient(id = '') {
    const c = db.clients.find(x=>x.id===id); const p=c?.pets?.[0] || {};
    openModal(c ? 'Client & patient profile' : 'Add client & patient', 'Contact and patient details stay connected in one care record.', `<div class="form-grid"><label>Client name<input name="name" value="${escapeHTML(c?.name||'')}" required></label><label>Mobile number<input name="phone" value="${escapeHTML(c?.phone||'')}" required></label><label>Email address<input name="email" type="email" value="${escapeHTML(c?.email||'')}" required></label><label>City / address<input name="address" value="${escapeHTML(c?.address||'')}" required></label><label>Patient name<input name="pet" value="${escapeHTML(p.name||'')}" required></label><label>Species<select name="species"><option>Dog</option><option ${p.species==='Cat'?'selected':''}>Cat</option><option ${p.species==='Rabbit'?'selected':''}>Rabbit</option><option>Bird</option><option>Other</option></select></label><label>Breed<input name="breed" value="${escapeHTML(p.breed||'')}" required></label><label>Sex<select name="sex"><option>Male</option><option ${p.sex==='Female'?'selected':''}>Female</option></select></label><label>Birthday<input type="date" name="birthday" value="${p.birthday||''}"></label><label class="checkbox-label"><input type="checkbox" name="consent" ${c?.consent?'checked':''}> Client consents to receive relevant clinic reminders and promotional communications.</label></div>`, fd => {
      const species=fd.get('species'); const emoji={Dog:'🐶',Cat:'🐱',Rabbit:'🐰',Bird:'🐦'}[species]||'🐾';
      const record={id:c?.id||`CL-${1050+db.clients.length}`,name:fd.get('name'),phone:fd.get('phone'),email:fd.get('email'),address:fd.get('address'),consent:fd.get('consent')==='on',lastVisit:c?.lastVisit||'',pets:[{name:fd.get('pet'),species,breed:fd.get('breed'),sex:fd.get('sex'),birthday:fd.get('birthday'),emoji}]};
      c ? Object.assign(c,record) : db.clients.unshift(record); recordActivity('users',c?'Client profile updated':'New client added',`${record.name} · ${record.pets[0].name}`); saveData(); navigate('clients'); toast(c?'Profile updated':'Client and patient added');
    }, true);
  }

  function openAppointment(id = '') {
    const a=db.appointments.find(x=>x.id===id);
    openModal(a?'Update appointment':'Book appointment','Record the visit details needed by the front desk and care team.',`<div class="form-grid"><label>Client & patient<select name="clientId" required>${db.clients.map(c=>`<option value="${c.id}" ${a?.clientId===c.id?'selected':''}>${escapeHTML(c.name)} — ${escapeHTML(c.pets[0].name)}</option>`).join('')}</select></label><label>Service<select name="service"><option>Annual wellness exam</option><option ${a?.service==='Core vaccination'?'selected':''}>Core vaccination</option><option ${a?.service==='Dermatology follow-up'?'selected':''}>Dermatology follow-up</option><option ${a?.service==='Dental assessment'?'selected':''}>Dental assessment</option><option ${a?.service==='Senior care assessment'?'selected':''}>Senior care assessment</option><option ${a?.service==='Nail trim & wellness'?'selected':''}>Nail trim & wellness</option></select></label><label>Date<input type="date" name="date" min="${todayISO()}" value="${a?.date||todayISO()}" required></label><label>Time<input type="time" name="time" value="${a?.time||'09:00'}" required></label><label>Veterinarian<select name="veterinarian"><option>Dr. Mia Garcia</option><option ${a?.veterinarian==='Dr. Luis Mendoza'?'selected':''}>Dr. Luis Mendoza</option><option ${a?.veterinarian==='Dr. Ava Villanueva'?'selected':''}>Dr. Ava Villanueva</option></select></label><label>Status<select name="status">${['Scheduled','Confirmed','Checked in','Completed','Cancelled','Pending'].map(s=>`<option ${a?.status===s?'selected':''}>${s}</option>`).join('')}</select></label><label class="span-2">Appointment notes<textarea name="notes" placeholder="Reason for visit, preparation, or client request…">${escapeHTML(a?.notes||'')}</textarea></label></div>`,fd=>{const c=db.clients.find(x=>x.id===fd.get('clientId'));const p=c.pets[0];const record={id:a?.id||`AP-${2093+db.appointments.length}`,date:fd.get('date'),time:fd.get('time'),clientId:c.id,client:c.name,pet:p.name,emoji:p.emoji,service:fd.get('service'),veterinarian:fd.get('veterinarian'),status:fd.get('status'),notes:fd.get('notes')};a?Object.assign(a,record):db.appointments.push(record);recordActivity('calendar',a?'Appointment updated':'Appointment booked',`${record.pet} · ${record.service}`);saveData();navigate('appointments');toast(a?'Appointment updated':'Appointment booked');},true);
  }

  function openInquiry(id = '') {
    const q=db.inquiries.find(x=>x.id===id);
    openModal(q?'Inquiry record':'Log client inquiry','Structured fields turn every conversation into useful follow-up and campaign data.',`<div class="form-grid"><label>Client or lead name<input name="client" value="${escapeHTML(q?.client||'')}" required></label><label>Contact detail<input name="contact" value="${escapeHTML(q?.contact||'')}" placeholder="Phone or email" required></label><label>Patient name<input name="pet" value="${escapeHTML(q?.pet||'')}" required></label><label>Inquiry channel<select name="channel">${['Phone','Website','Messenger','Email','Walk-in','SMS'].map(v=>`<option ${q?.channel===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Category<select name="category">${['Appointment','New client','Post-visit care','Billing','Services & pricing','Promotion','Other'].map(v=>`<option ${q?.category===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Priority<select name="priority">${['Low','Medium','High','Urgent'].map(v=>`<option ${q?.priority===v?'selected':''}>${v}</option>`).join('')}</select><span class="field-help">Urgent concerns should be escalated to the clinical team.</span></label><label class="span-2">Subject<input name="subject" value="${escapeHTML(q?.subject||'')}" required></label><label class="span-2">Conversation details<textarea name="details" required placeholder="Capture the question, guidance given, and any promise made…">${escapeHTML(q?.details||'')}</textarea></label><label>Preferred response channel<select name="preferred">${['Phone','SMS','Email','Messenger'].map(v=>`<option ${q?.preferred===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Follow-up date<input type="date" name="followUp" value="${q?.followUp||addDays(1)}"></label><label>Status<select name="status">${['New','Open','Follow-up','Resolved','Closed'].map(v=>`<option ${q?.status===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Assigned owner<input name="owner" value="${escapeHTML(q?.owner||users[session.role].name)}" required></label><label class="span-2 checkbox-label"><input type="checkbox" name="consent" ${q?.consent?'checked':''}> Client consented to receive follow-up and relevant promotional messages on the preferred channel.</label></div>`,fd=>{const record={id:q?.id||`INQ-${3022+db.inquiries.length}`,created:q?.created||todayISO(),client:fd.get('client'),contact:fd.get('contact'),pet:fd.get('pet'),channel:fd.get('channel'),category:fd.get('category'),subject:fd.get('subject'),details:fd.get('details'),priority:fd.get('priority'),status:fd.get('status'),preferred:fd.get('preferred'),consent:fd.get('consent')==='on',owner:fd.get('owner'),followUp:fd.get('followUp')};q?Object.assign(q,record):db.inquiries.unshift(record);recordActivity('message',q?'Inquiry updated':'Inquiry logged',`${record.client} · ${record.category}`);saveData();navigate('inquiries');toast(q?'Inquiry updated':'Inquiry logged and assigned');},true);
  }

  function openReminder(id = '') {
    const r=db.reminders.find(x=>x.id===id);
    openModal(r?'Edit reminder':'Schedule reminder','Plan the next client touchpoint and keep communication consistent.',`<div class="form-grid"><label>Client name<input name="client" value="${escapeHTML(r?.client||'')}" required></label><label>Patient name<input name="pet" value="${escapeHTML(r?.pet||'')}" required></label><label>Reminder type<select name="type">${['Appointment reminder','Post-visit check-in','Vaccination reminder','Medication follow-up','Wellness recall'].map(v=>`<option ${r?.type===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Channel<select name="channel">${['SMS','Email','Phone','Messenger'].map(v=>`<option ${r?.channel===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Due date<input type="date" name="due" value="${r?.due||addDays(1)}" required></label><label>Status<select name="status">${['Pending','Scheduled','Sent'].map(v=>`<option ${r?.status===v?'selected':''}>${v}</option>`).join('')}</select></label><label class="span-2">Message<textarea name="message" required>${escapeHTML(r?.message||'')}</textarea></label></div>`,fd=>{const record={id:r?.id||uid('REM'),client:fd.get('client'),pet:fd.get('pet'),type:fd.get('type'),channel:fd.get('channel'),due:fd.get('due'),status:fd.get('status'),message:fd.get('message')};r?Object.assign(r,record):db.reminders.unshift(record);recordActivity('bell',r?'Reminder updated':'Reminder scheduled',`${record.client} · ${record.type}`);saveData();navigate('reminders');toast(r?'Reminder updated':'Reminder scheduled');});
  }

  function openPromotion(id = '') {
    const p=db.promotions.find(x=>x.id===id);
    openModal(p?'Manage promotional offer':'Create promotional offer','Define a measurable, consent-aware campaign for the right client segment.',`<div class="form-grid"><label>Campaign name<input name="name" value="${escapeHTML(p?.name||'')}" required></label><label>Offer code<input name="code" value="${escapeHTML(p?.code||'')}" placeholder="PAWCARE20" required></label><label>Offer value<input name="type" value="${escapeHTML(p?.type||'')}" placeholder="20% off" required></label><label>Audience segment<select name="audience"><option>New puppy leads</option><option ${p?.audience==='Senior pets (7+ years)'?'selected':''}>Senior pets (7+ years)</option><option ${p?.audience==='Dogs & cats due for dental care'?'selected':''}>Dogs & cats due for dental care</option><option>Inactive clients (6+ months)</option><option>All consented clients</option></select></label><label>Delivery channel<select name="channel"><option>SMS + Email</option><option ${p?.channel==='Email'?'selected':''}>Email</option><option ${p?.channel==='SMS'?'selected':''}>SMS</option><option>Messenger</option></select></label><label>Status<select name="status">${['Draft','Scheduled','Active','Completed'].map(v=>`<option ${p?.status===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Start date<input type="date" name="start" value="${p?.start||addDays(2)}" required></label><label>End date<input type="date" name="end" value="${p?.end||addDays(32)}" required></label><label>Approval<select name="approval"><option>Needs review</option><option ${p?.approval==='Approved'?'selected':''}>Approved</option></select><span class="field-help">Offers should be approved before activation.</span></label><label>Created by<input name="createdBy" value="${escapeHTML(p?.createdBy||users[session.role].name)}" readonly></label><label class="span-2">Campaign goal<textarea name="goal" required placeholder="What outcome should this offer create?">${escapeHTML(p?.goal||'')}</textarea></label><label class="span-2 checkbox-label"><input type="checkbox" required checked> Send only to clients with valid communication consent for the selected channel.</label></div>`,fd=>{const record={id:p?.id||uid('PRO'),name:fd.get('name'),code:fd.get('code').toUpperCase(),type:fd.get('type'),audience:fd.get('audience'),channel:fd.get('channel'),start:fd.get('start'),end:fd.get('end'),status:fd.get('status'),approval:fd.get('approval'),goal:fd.get('goal'),reach:p?.reach||0,claims:p?.claims||0,conversion:p?.conversion||0,createdBy:fd.get('createdBy')};p?Object.assign(p,record):db.promotions.unshift(record);recordActivity('tag',p?'Promotion updated':'Promotion created',`${record.name} · ${record.audience}`);saveData();navigate('promotions');toast(p?'Offer updated':'Promotional offer created');},true);
  }

  function openFeedback(id = '') {
    const f=db.feedback.find(x=>x.id===id);
    openModal(f?'Review feedback':'Record client feedback','Document the experience signal and the manager response.',`<div class="form-grid"><label>Client name<input name="client" value="${escapeHTML(f?.client||'')}" required></label><label>Patient name<input name="pet" value="${escapeHTML(f?.pet||'')}" required></label><label>Rating<select name="rating">${[5,4,3,2,1].map(v=>`<option value="${v}" ${Number(f?.rating)===v?'selected':''}>${v} — ${v===5?'Excellent':v===4?'Good':v===3?'Average':v===2?'Poor':'Very poor'}</option>`).join('')}</select></label><label>Category<select name="category">${['Service','Veterinarian','Wait time','Billing','Facilities','Follow-up'].map(v=>`<option ${f?.category===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Status<select name="status">${['Open','Escalated','Reviewed','Closed'].map(v=>`<option ${f?.status===v?'selected':''}>${v}</option>`).join('')}</select></label><label>Escalation<select name="escalation">${['None','Manager review','Urgent'].map(v=>`<option ${f?.escalation===v?'selected':''}>${v}</option>`).join('')}</select></label><label class="span-2">Client comment<textarea name="comment" required>${escapeHTML(f?.comment||'')}</textarea></label></div>`,fd=>{const record={id:f?.id||uid('FB'),date:f?.date||todayISO(),client:fd.get('client'),pet:fd.get('pet'),rating:Number(fd.get('rating')),category:fd.get('category'),comment:fd.get('comment'),status:fd.get('status'),escalation:fd.get('escalation')};f?Object.assign(f,record):db.feedback.unshift(record);recordActivity('alert',f?'Feedback reviewed':'Feedback recorded',`${record.client} · ${record.category}`);saveData();navigate('feedback');toast(f?'Feedback response saved':'Feedback recorded');});
  }

  function exportData(type) {
    const payload = type === 'all' ? db : db[type];
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob); const a=document.createElement('a');
    a.href=url;a.download=`pawcare-${type}-${todayISO()}.json`;a.click();URL.revokeObjectURL(url);toast(`${type === 'all' ? 'CRM' : type} data exported`);
  }

  function initAppChrome() {
    $('#logout-btn').addEventListener('click', () => { saveSession(null); $('#app-view').hidden=true; $('#login-view').hidden=false; $('#login-password').value='pawcare123'; });
    $('#open-menu').addEventListener('click', () => $('#sidebar').classList.add('is-open'));
    $('#close-menu').addEventListener('click', () => $('#sidebar').classList.remove('is-open'));
    $('#global-search').addEventListener('keydown', e => {
      if (e.key === 'Enter' && e.currentTarget.value.trim()) {
        const term=e.currentTarget.value.trim().toLowerCase();
        if (db.clients.some(c=>`${c.name} ${c.pets[0].name}`.toLowerCase().includes(term))) navigate('clients');
        else if (db.inquiries.some(q=>`${q.client} ${q.pet} ${q.subject}`.toLowerCase().includes(term)) && session.role==='receptionist') navigate('inquiries');
        else navigate('appointments');
        setTimeout(()=>{const search=$('[data-table-search]');if(search){search.value=term;search.dispatchEvent(new Event('input'));}},30);
      }
    });
    document.addEventListener('keydown', e => { if ((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k') { e.preventDefault(); $('#global-search').focus(); } if(e.key==='Escape'&&$('.modal-backdrop')) $('#modal-root').innerHTML=''; });
  }

  initLogin();
  initAppChrome();
  if (session && users[session.role]) showApp();
})();
