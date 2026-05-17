import { api } from '../../api/client.js';
import { AppState } from '../../app.js';

window._api = api;

const params = new URLSearchParams(location.search);
const productId = params.get('id');
let courseData = null;
let isReadOnly = false;

// Load categories
async function loadCategories() {
  const cats = await api.get('/products/categories');
  const sel = document.getElementById('f-category');
  sel.innerHTML = '<option value="">-- Chọn danh mục --</option>' +
    cats.map(c => `<option value="${c.category_id}">${c.name}</option>`).join('');
}

// Load existing course if editing
function initTypeToggle() {
  const typeSelect = document.getElementById('f-type');
  const courseFields = document.getElementById('course-fields');
  const ebookFields = document.getElementById('ebook-fields');
  const tabCurriculumBtn = document.querySelector('[onclick="switchTab(\'curriculum\', this)"]');

  function toggle() {
    if (typeSelect.value === 'course') {
      courseFields.style.display = 'block';
      ebookFields.style.display = 'none';
      if(tabCurriculumBtn) tabCurriculumBtn.style.display = 'inline-block';
    } else {
      courseFields.style.display = 'none';
      ebookFields.style.display = 'block';
      if(tabCurriculumBtn) tabCurriculumBtn.style.display = 'none';
      const curTab = document.getElementById('tab-curriculum');
      if (curTab && curTab.classList.contains('active')) {
        window.switchTab('info', document.querySelector('[onclick="switchTab(\'info\', this)"]'));
      }
    }
  }
  
  typeSelect.addEventListener('change', toggle);
  toggle();
}

async function loadCourse() {
  if (!productId) {
    initTypeToggle();
    return;
  }
  document.getElementById('page-title').textContent = '✏️ Chỉnh sửa khóa học';
  document.getElementById('btn-save-draft').textContent = '💾 Lưu thay đổi';

  try {
    const data = await api.get(`/instructor/courses/${productId}`, true);
    courseData = data;

    document.getElementById('f-name').value = data.name || '';
    document.getElementById('f-price').value = data.price || '';
    document.getElementById('f-original-price').value = data.original_price || '';
    document.getElementById('f-type').value = data.product_type || 'course';
    document.getElementById('f-thumbnail').value = data.thumbnail_url || '';
    document.getElementById('f-short-desc').value = data.short_description || '';
    document.getElementById('f-desc').value = data.description || '';
    
    if (data.product_type === 'course' && data.course) {
      document.getElementById('f-level').value = data.course.level || '';
      document.getElementById('f-duration').value = data.course.duration || 0;
      
      let reqs = data.course.requirements;
      let learn = data.course.what_you_learn;
      
      try { if (typeof reqs === 'string' && reqs.startsWith('[')) reqs = JSON.parse(reqs); } catch(e){}
      try { if (typeof learn === 'string' && learn.startsWith('[')) learn = JSON.parse(learn); } catch(e){}
      
      document.getElementById('f-requirements').value = Array.isArray(reqs) ? reqs.join('\n') : (reqs || '');
      document.getElementById('f-learn').value = Array.isArray(learn) ? learn.join('\n') : (learn || '');
    } else if (data.product_type === 'ebook' && data.ebook) {
      document.getElementById('f-ebook-format').value = data.ebook.format || 'pdf';
      document.getElementById('f-ebook-pages').value = data.ebook.page_count || 0;
      document.getElementById('f-ebook-size').value = data.ebook.file_size || 0;
    }

    if (data.category_id) {
      document.getElementById('f-category').value = data.category_id;
    }
    
    document.getElementById('f-type').value = data.product_type || 'course';
    initTypeToggle();
    updateThumbPreview(data.thumbnail_url);

    // Status handling
    const status = data.status;
    isReadOnly = status === 'pending_approval';

    const statusInfo = {
      draft: ['📝 Bản nháp', '#6366f1'],
      pending_approval: ['⏳ Chờ duyệt', '#f59e0b'],
      active: ['✅ Đang bán', '#22c55e'],
      rejected: ['❌ Bị từ chối', '#ef4444'],
      inactive: ['🚫 Đã ẩn', '#9ca3af'],
    };
    const [slabel, scolor] = statusInfo[status] || [status, '#9ca3af'];
    const badge = document.getElementById('status-badge');
    badge.style.display = 'inline-block';
    badge.innerHTML = `<span style="background:${scolor}20;color:${scolor};padding:4px 12px;border-radius:20px;font-size:0.8rem;font-weight:600">${slabel}</span>`;

    // Banners
    const banner = document.getElementById('status-banner');
    if (status === 'pending_approval') {
      banner.innerHTML = `<div class="status-banner banner-pending">⏳ <strong>Đang chờ Admin kiểm duyệt.</strong> Bạn không thể chỉnh sửa trong thời gian này.</div>`;
      document.getElementById('btn-save-draft').disabled = true;
      document.getElementById('curriculum-locked-banner').style.display = 'flex';
    } else if (status === 'active') {
      banner.innerHTML = `<div class="status-banner banner-active">✅ <strong>Khóa học đang bán trên hệ thống.</strong> Nếu bạn lưu thay đổi, khóa học sẽ tạm ẩn và chờ Admin duyệt lại.</div>`;
    } else if (status === 'rejected') {
      banner.innerHTML = `<div class="status-banner banner-rejected">❌ <strong>Khóa học bị từ chối.</strong>${data.rejection_reason ? ` Lý do: <em>${data.rejection_reason}</em>` : ''} Hãy sửa và gửi duyệt lại.</div>`;
    }

    if (['draft', 'rejected'].includes(status)) {
      document.getElementById('btn-submit').style.display = 'inline-flex';
    }

    if (data.product_type === 'course') {
      renderModules(data.course?.modules || []);
    }
  } catch (err) {
    app.showToast('Không thể tải khóa học: ' + err.message, 'error');
  }
}

function updateThumbPreview(url) {
  const preview = document.getElementById('thumb-preview');
  const img = document.getElementById('thumb-img');
  if (url) { img.src = url; preview.style.display = 'block'; }
  else { preview.style.display = 'none'; }
}
document.getElementById('f-thumbnail').addEventListener('input', e => updateThumbPreview(e.target.value));

window.switchTab = function(tab, btn) {
  document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.tab-content').forEach(t => t.classList.remove('active'));
  btn.classList.add('active');
  document.getElementById('tab-' + tab).classList.add('active');
};

// Save course
window.saveCourse = async function() {
  const btn = document.getElementById('btn-save-draft');
  btn.disabled = true;
  const origText = btn.textContent;
  btn.innerHTML = '<div class="spinner-sm" style="display:inline-block;margin-right:6px"></div> Đang lưu...';

  const payload = {
    name: document.getElementById('f-name').value.trim(),
    price: parseFloat(document.getElementById('f-price').value) || 0,
    original_price: parseFloat(document.getElementById('f-original-price').value) || null,
    description: document.getElementById('f-desc').value.trim() || null,
    short_description: document.getElementById('f-short-desc').value.trim() || null,
    thumbnail_url: document.getElementById('f-thumbnail').value.trim() || null,
    category_id: parseInt(document.getElementById('f-category').value) || null,
    product_type: document.getElementById('f-type').value,
  };

  if (payload.product_type === 'course') {
    payload.level = document.getElementById('f-level').value || null;
    payload.duration = parseInt(document.getElementById('f-duration').value) || null;
    
    const reqs = document.getElementById('f-requirements').value.trim();
    const learn = document.getElementById('f-learn').value.trim();
    
    payload.requirements = reqs ? JSON.stringify(reqs.split('\n').map(s => s.trim()).filter(s => s)) : null;
    payload.what_you_learn = learn ? JSON.stringify(learn.split('\n').map(s => s.trim()).filter(s => s)) : null;
  } else if (payload.product_type === 'ebook') {
    payload.format = document.getElementById('f-ebook-format').value || null;
    payload.page_count = parseInt(document.getElementById('f-ebook-pages').value) || null;
    payload.file_size = parseFloat(document.getElementById('f-ebook-size').value) || null;
  }

  if (!payload.name) { app.showToast('Vui lòng nhập tên khóa học', 'error'); btn.disabled = false; btn.textContent = origText; return; }

  try {
    if (productId) {
      await api.put(`/instructor/courses/${productId}`, payload, true);
      app.showToast('Đã lưu thay đổi! ✅', 'success');
      setTimeout(() => location.reload(), 1000);
    } else {
      const res = await api.post('/instructor/courses', payload, true);
      app.showToast('Tạo khóa học thành công! Tiếp tục thêm bài học 📚', 'success');
      setTimeout(() => { location.href = `/instructor/course-editor.html?id=${res.product_id}`; }, 1000);
    }
  } catch (err) {
    app.showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.textContent = origText;
  }
};

window.submitForReview = async function() {
  if (!productId) { app.showToast('Hãy lưu khóa học trước', 'error'); return; }
  const isConfirmed = await window.app.showConfirm('Gửi kiểm duyệt', 'Gửi khóa học này lên để Admin kiểm duyệt? Bạn sẽ không thể chỉnh sửa cho đến khi có kết quả.');
  if (!isConfirmed) return;
  try {
    await api.post(`/instructor/courses/${productId}/submit`, null, true);
    app.showToast('Đã gửi kiểm duyệt! Admin sẽ xem xét sớm 🚀', 'success');
    setTimeout(() => location.reload(), 1200);
  } catch (err) {
    app.showToast(err.message, 'error');
  }
};

// ── Curriculum ──
async function _reloadContent() {
  if (!productId) return;
  try {
    const data = await api.get(`/instructor/courses/${productId}`, true);
    if (data.course) {
      renderModules(data.course.modules || []);
    }
  } catch(e) { app.showToast(e.message, 'error'); }
}

function renderModules(modules) {
  const container = document.getElementById('modules-container');
  if (!modules.length) {
    container.innerHTML = `<div style="text-align:center;padding:40px;color:var(--color-text-muted)">
      <div style="font-size:2rem;margin-bottom:12px">📭</div>
      <div>Chưa có module nào. Nhấn "+ Thêm Chương mới" để bắt đầu.</div>
    </div>`;
    return;
  }
  container.innerHTML = modules.map((m, mIdx) => `
    <div style="margin-bottom:16px;border:1px solid var(--color-border);border-radius:10px;overflow:hidden">
      <div style="padding:14px 16px;background:var(--color-bg-glass);display:flex;align-items:center;gap:10px">
        <span style="font-weight:700;flex:1">📁 ${m.title}</span>
        <span style="font-size:0.75rem;color:var(--color-text-muted)">${m.lessons.length} bài</span>
        ${!isReadOnly ? `
        <button class="btn btn-sm btn-secondary" onclick="showAddLessonForm(${m.module_id})">+ Bài học</button>
        <button class="btn btn-sm btn-ghost" title="Sửa module" onclick="editModuleInline(${m.module_id}, '${m.title.replace(/'/g,'')}')">📝</button>
        <button class="btn btn-sm btn-ghost" title="Xóa module" onclick="deleteModule(${m.module_id})" style="color:var(--color-error)">🗑</button>
        ` : ''}
      </div>
      <div style="padding:8px 0">
        ${m.lessons.length === 0 ? `<div style="padding:12px 20px;font-size:0.85rem;color:var(--color-text-muted)">Chưa có bài học</div>` :
          m.lessons.map((l, lIdx) => `
          <div style="padding:10px 20px;display:flex;align-items:center;gap:10px;border-bottom:1px solid rgba(255,255,255,0.04)">
            <span style="font-size:0.75rem;color:var(--color-text-muted);width:24px;text-align:center">${lIdx+1}</span>
            <span style="flex:1;font-size:0.9rem">${l.title}</span>
            ${l.mux_playback_id ? `<span style="font-size:0.7rem;background:rgba(99,102,241,0.2);color:var(--color-accent-light);padding:2px 8px;border-radius:4px">🎬 ${l.mux_playback_id.slice(0,12)}...</span>` :
              `<span style="font-size:0.7rem;color:var(--color-text-muted);background:rgba(255,255,255,0.06);padding:2px 8px;border-radius:4px">⚠️ Chưa có video</span>`}
            ${l.is_preview ? `<span style="font-size:0.7rem;color:var(--color-accent)">preview</span>` : ''}
            <span style="font-size:0.75rem;color:var(--color-text-muted)">${l.duration ? Math.round(l.duration/60)+'m' : '--'}</span>
            ${!isReadOnly ? `
            <button class="btn btn-sm btn-ghost" title="Sửa lesson" onclick="editLessonModal(${l.lesson_id}, '${l.title.replace(/'/g,'')}', '${l.mux_playback_id || ''}', ${l.duration||0}, ${l.is_preview})">✏️</button>
            <button class="btn btn-sm btn-ghost" title="Xóa" onclick="deleteLesson(${l.lesson_id})" style="color:var(--color-error)">🗑</button>
            ` : ''}
          </div>`).join('')}
      </div>
    </div>`).join('');
}

window.addModule = async function() {
  if (isReadOnly) return;
  if (!productId) { app.showToast('Hãy lưu khóa học trước khi thêm chương', 'warning'); return; }
  const title = prompt('Tên chương mới:');
  if (!title?.trim()) return;
  try {
    const sortOrder = document.querySelectorAll('#modules-container > div').length;
    await api.post(`/instructor/courses/${productId}/modules?title=${encodeURIComponent(title.trim())}&sort_order=${sortOrder}`, null, true);
    app.showToast('Đã thêm chương mới! ✅', 'success');
    _reloadContent();
  } catch (err) {
    app.showToast(err.message, 'error');
  }
};

window.editModuleInline = async (moduleId, currentTitle) => {
  if (isReadOnly) return;
  const newTitle = prompt('Sửa tên module:', currentTitle);
  if (!newTitle || newTitle === currentTitle) return;
  try {
    await api.put(`/instructor/modules/${moduleId}?title=${encodeURIComponent(newTitle)}`, null, true);
    app.showToast('Đã sửa module ✅', 'success');
    _reloadContent();
  } catch(e) { app.showToast(e.message, 'error'); }
};

window.deleteModule = async function(moduleId) {
  if (isReadOnly) return;
  const isConfirmed = await window.app.showConfirm('Xóa chương', 'Xóa chương này và tất cả bài học trong đó?', 'Xóa', true);
  if (!isConfirmed) return;
  try {
    await api.delete(`/instructor/modules/${moduleId}`, true);
    app.showToast('Đã xóa chương ✅', 'success');
    _reloadContent();
  } catch (err) {
    app.showToast(err.message, 'error');
  }
};

window.showAddLessonForm = (moduleId) => {
  if (isReadOnly) return;
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.style.zIndex = '1100';
  overlay.innerHTML = `
    <div class="modal" style="max-width:520px">
      <div class="modal__header">
        <div class="modal__title">➕ Thêm bài học mới</div>
        <div class="modal__close" onclick="this.closest('.modal-overlay').remove()">✕</div>
      </div>
      <form id="add-lesson-form">
        <div class="form-group"><label class="form-label">Tên bài học <span style="color:var(--color-error)">*</span></label>
          <input class="form-control" name="title" placeholder="VD: Giới thiệu về FastAPI" required></div>
        <div class="form-group">
          <label class="form-label">Mux Playback ID</label>
          <input class="form-control" name="mux_playback_id" placeholder="VD: qU1jw1sfGTK...">
          <div style="font-size:0.75rem;color:var(--color-text-muted);margin-top:6px">
            💡 Lấy từ <a href="https://dashboard.mux.com" target="_blank" style="color:var(--color-accent-light)">Mux Dashboard → Tài sản → Playback ID</a>
          </div>
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
          <div class="form-group"><label class="form-label">Thời lượng (giây)</label>
            <input type="number" class="form-control" name="duration" value="0" min="0"></div>
          <div class="form-group"><label class="form-label">Thứ tự</label>
            <input type="number" class="form-control" name="sort_order" value="0" min="0"></div>
        </div>
        <div class="form-group" style="display:flex;align-items:center;gap:10px">
          <input type="checkbox" id="is-preview-check" name="is_preview">
          <label for="is-preview-check" class="form-label" style="margin:0">Cho xem thử miễn phí</label>
        </div>
        <button type="submit" class="btn btn-primary btn-block">Thêm bài học</button>
      </form>
    </div>`;
  document.body.appendChild(overlay);
  document.getElementById('add-lesson-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    const q = new URLSearchParams({
      title: fd.get('title'),
      mux_playback_id: fd.get('mux_playback_id') || '',
      duration: fd.get('duration') || 0,
      sort_order: fd.get('sort_order') || 0,
      is_preview: fd.get('is_preview') === 'on' ? 'true' : 'false',
    });
    try {
      await api.post(`/instructor/modules/${moduleId}/lessons?${q}`, null, true);
      app.showToast('Đã thêm bài học ✅', 'success');
      overlay.remove();
      _reloadContent();
    } catch(err) { app.showToast(err.message, 'error'); }
  });
};

window.editLessonModal = (lessonId, currentTitle, currentMuxId, duration, isPreview) => {
  if (isReadOnly) return;
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.style.zIndex = '1100';
  overlay.innerHTML = `
    <div class="modal" style="max-width:520px">
      <div class="modal__header">
        <div class="modal__title">✏️ Sửa bài học</div>
        <div class="modal__close" onclick="this.closest('.modal-overlay').remove()">✕</div>
      </div>
      <form id="edit-lesson-form">
        <div class="form-group"><label class="form-label">Tên bài học <span style="color:var(--color-error)">*</span></label>
          <input class="form-control" name="title" value="${currentTitle}" required></div>
        <div class="form-group">
          <label class="form-label">Mux Playback ID</label>
          <input class="form-control" name="mux_playback_id" value="${currentMuxId}">
        </div>
        <div style="display:grid;grid-template-columns:1fr;gap:16px">
          <div class="form-group"><label class="form-label">Thời lượng (giây)</label>
            <input type="number" class="form-control" name="duration" value="${duration}" min="0"></div>
        </div>
        <div class="form-group" style="display:flex;align-items:center;gap:10px">
          <input type="checkbox" id="is-preview-check" name="is_preview" ${isPreview ? 'checked' : ''}>
          <label for="is-preview-check" class="form-label" style="margin:0">Cho xem thử miễn phí</label>
        </div>
        <button type="submit" class="btn btn-primary btn-block">Lưu thay đổi</button>
      </form>
    </div>`;
  document.body.appendChild(overlay);
  document.getElementById('edit-lesson-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    const q = new URLSearchParams({
      title: fd.get('title'),
      mux_playback_id: fd.get('mux_playback_id') || '',
      duration: fd.get('duration') || 0,
      is_preview: fd.get('is_preview') === 'on' ? 'true' : 'false',
    });
    try {
      await api.put(`/instructor/lessons/${lessonId}?${q}`, null, true);
      app.showToast('Đã sửa bài học ✅', 'success');
      overlay.remove();
      _reloadContent();
    } catch(err) { app.showToast(err.message, 'error'); }
  });
};

window.deleteLesson = async function(lessonId) {
  if (isReadOnly) return;
  const isConfirmed = await window.app.showConfirm('Xóa bài học', 'Xóa bài học này?', 'Xóa', true);
  if (!isConfirmed) return;
  try {
    await api.delete(`/instructor/lessons/${lessonId}`, true);
    app.showToast('Đã xóa bài học ✅', 'success');
    _reloadContent();
  } catch (err) {
    app.showToast(err.message, 'error');
  }
};

setTimeout(async () => {
  const user = window.AppState?.user || JSON.parse(localStorage.getItem('el_user') || 'null');
  if (!user || !['author', 'admin'].includes(user.role)) {
    window.location.href = '/';
    return;
  }
  await loadCategories();
  await loadCourse();
}, 100);
