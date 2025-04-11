let currentUsername = null;
let currentSpace = null;
let currentFiles = [];
let uploadInProgress = false;
let uploadController = null;
let selectedFiles = [];
let currentUploadIndex = 0;
let selectedFileNames = new Set();
const authContainer = document.getElementById('authContainer');
const mainContainer = document.getElementById('mainContainer');
const spaceList = document.getElementById('spaceList');
const fileList = document.getElementById('fileList');
const currentSpaceElement = document.getElementById('currentSpace');
const currentUserElement = document.getElementById('currentUser');
const userInfoElement = document.getElementById('userInfo');
const fileInput = document.getElementById('fileInput');
const uploadProgress = document.getElementById('uploadProgress');
const uploadProgressContainer = document.getElementById('uploadProgressContainer');
const uploadStatus = document.getElementById('uploadStatus');
const previewContainer = document.getElementById('previewContainer');
const previewContent = document.getElementById('previewContent');
const previewFileName = document.getElementById('previewFileName');
const createSpaceModal = document.getElementById('createSpaceModal');
const dropZone = document.getElementById('dropZone');
const uploadBtn = document.getElementById('uploadBtn');
const cancelUploadBtn = document.getElementById('cancelUploadBtn');
const selectedFilesContainer = document.getElementById('selectedFiles');
const fileListContainer = document.getElementById('fileListContainer');
const fileCountElement = document.getElementById('fileCount');
const totalSizeElement = document.getElementById('totalSize');
const currentUploadFileElement = document.getElementById('currentUploadFile');
const uploadPercentageElement = document.getElementById('uploadPercentage');
const searchInput = document.getElementById('searchInput');
const searchContentCheckbox = document.getElementById('searchContent');
const multiActions = document.getElementById('multiActions');
const publishFilesContainer = document.getElementById('publicFilesContainer');
let currentFileId = null;
let viewedUsername = null;
function transliterate(text) {
    const ru = {'а':'a','б':'b','в':'v','г':'g','д':'d','е':'e','ё':'e','ж':'zh','з':'z','и':'i','й':'i','к':'k','л':'l','м':'m','н':'n','о':'o','п':'p','р':'r','с':'s','т':'t','у':'u','ф':'f','х':'h','ц':'c','ч':'ch','ш':'sh','щ':'sh','ы':'y','э':'e','ю':'yu','я':'ya'};
    return text.split('').map(char => ru[char] || char).join('');
}
function toggleTheme() {
    const body = document.body;
    const themeToggle = document.getElementById('themeToggle');
    const themeToggleSettings = document.getElementById('themeToggleSettings');
    
    if (body.classList.contains('light-theme')) {
        body.classList.remove('light-theme');
        localStorage.setItem('theme', 'dark');
        if (themeToggle) themeToggle.innerHTML = '<i class="fas fa-moon"></i> Dark Mode';
        if (themeToggleSettings) themeToggleSettings.innerHTML = '<i class="fas fa-moon"></i> Dark Mode';
    } else {
        body.classList.add('light-theme');
        localStorage.setItem('theme', 'light');
        if (themeToggle) themeToggle.innerHTML = '<i class="fas fa-sun"></i> Light Mode';
        if (themeToggleSettings) themeToggleSettings.innerHTML = '<i class="fas fa-sun"></i> Light Mode';
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const savedTheme = localStorage.getItem('theme') || 'dark';
    const themeToggle = document.getElementById('themeToggle');
    const storedUser = localStorage.getItem('melanCloudUser');
    if (savedTheme === 'light') {
        document.body.classList.add('light-theme');
        themeToggle.innerHTML = '<i class="fas fa-sun"></i> Light Mode';
    }
    if (storedUser) {
        currentUsername = storedUser;
        showMain();
    } else {
        showAuth();
    }
    initDragAndDrop();

    let searchTimeout;
    searchInput.addEventListener('input', () => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(filterFiles, 300);
    });
    searchContentCheckbox.addEventListener('change', filterFiles);
});

function getFileIcon(filename) {
    if (!filename || typeof filename !== 'string') {
        return 'fa-file';
    }

    const extension = filename.substring(filename.lastIndexOf('.')).toLowerCase();
    const icons = {
        '.jpg': 'fa-file-image', '.jpeg': 'fa-file-image', '.png': 'fa-file-image', '.gif': 'fa-file-image',
        '.mp4': 'fa-file-video', '.mov': 'fa-file-video', '.webm': 'fa-file-video',
        '.mp3': 'fa-file-audio', '.wav': 'fa-file-audio', '.flac': 'fa-file-audio',
        '.txt': 'fa-file-alt', '.json': 'fa-file-code', '.xml': 'fa-file-code', '.csv': 'fa-file-csv',
        '.pdf': 'fa-file-pdf',
        '.zip': 'fa-file-archive', '.rar': 'fa-file-archive', '.7z': 'fa-file-archive',
        '.doc': 'fa-file-word', '.docx': 'fa-file-word',
        '.xls': 'fa-file-excel', '.xlsx': 'fa-file-excel',
        '.ppt': 'fa-file-powerpoint', '.pptx': 'fa-file-powerpoint'
    };
    return icons[extension] || 'fa-file';
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${units[i]}`;
}

function showAuth() {
    authContainer.classList.remove('hidden');
    mainContainer.classList.add('hidden');
}

function showMain() {
    authContainer.classList.add('hidden');
    mainContainer.classList.remove('hidden');
    publicFilesContainer.classList.add('hidden');
    loadUserInfo();
    checkAdminStatus();
    loadSpaces();
}

function showModal(modalId) {
    document.getElementById(modalId).classList.remove('hidden');
}

function closeModal(modalId) {
    document.getElementById(modalId).classList.add('hidden');
}

function showCreateSpaceModal() {
    document.getElementById('newSpaceName').value = '';
    document.getElementById('isPublic').checked = false;
    showModal('createSpaceModal');
}

function initDragAndDrop() {
    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
        dropZone.addEventListener(eventName, preventDefaults, false);
        document.body.addEventListener(eventName, preventDefaults, false);
    });

    ['dragenter', 'dragover'].forEach(eventName => {
        dropZone.addEventListener(eventName, highlight, false);
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropZone.addEventListener(eventName, unhighlight, false);
    });

    dropZone.addEventListener('drop', handleDrop, false);
}

function preventDefaults(e) {
    e.preventDefault();
    e.stopPropagation();
}

function highlight() {
    dropZone.classList.add('highlight');
}

function unhighlight() {
    dropZone.classList.remove('highlight');
}

async function handleDrop(e) {
    const dt = e.dataTransfer;
    const files = dt.files;
    
    if (files.length > 0) {
        const newFiles = Array.from(files);
        
        const canAddFiles = await checkStorageLimit(newFiles);
        if (!canAddFiles) return;

        newFiles.forEach(newFile => {
            const isDuplicate = selectedFiles.some(
                existingFile => existingFile.name === newFile.name && 
                            existingFile.size === newFile.size &&
                            existingFile.lastModified === newFile.lastModified
            );
            
            if (!isDuplicate) {
                selectedFiles.push(newFile);
            }
        });
        
        const dataTransfer = new DataTransfer();
        selectedFiles.forEach(file => dataTransfer.items.add(file));
        fileInput.files = dataTransfer.files;
        
        updateSelectedFilesList();
    }
}

function updateSelectedFilesList() {
    fileListContainer.innerHTML = '';
    
    if (selectedFiles.length === 0) {
        selectedFilesContainer.classList.add('hidden');
        return;
    }
    
    selectedFilesContainer.classList.remove('hidden');
    fileCountElement.textContent = selectedFiles.length;
    
    let totalSize = 0;
    
    selectedFiles.forEach((file, index) => {
        totalSize += file.size;
        
        const fileItem = document.createElement('div');
        fileItem.className = 'file-item';
        fileItem.innerHTML = `
            <i class="fas ${getFileIcon(file.name)}"></i>
            <div class="file-info">
                <div class="file-name" title="${file.name}">${file.name}</div>
                <div class="file-size">${formatFileSize(file.size)}</div>
                <progress class="file-progress" id="fileProgress${index}" max="100" value="0"></progress>
            </div>
            <i class="fas fa-times file-remove" onclick="removeSelectedFile(${index})"></i>
        `;
        fileListContainer.appendChild(fileItem);
    });
    
    totalSizeElement.textContent = formatFileSize(totalSize);
}

function removeSelectedFile(index) {
    selectedFiles.splice(index, 1);
    
    const dataTransfer = new DataTransfer();
    selectedFiles.forEach(file => dataTransfer.items.add(file));
    fileInput.files = dataTransfer.files;
    
    updateSelectedFilesList();
}

function show2FAForm() {
    document.getElementById('auth-form').classList.add('hidden');
    document.getElementById('2faForm').classList.remove('hidden');
}

function showAuthForm() {
    document.getElementById('auth-form').classList.remove('hidden');
    document.getElementById('2faForm').classList.add('hidden');
}

async function login() {
    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;

    if (!username || !password) {
        alert('Please enter both username and password.');
        return;
    }

    try {
        const response = await fetch('/api/filemanager/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                Username: username, 
                Password: password 
            })
        });

        if (response.ok) {
            const data = await response.json();
            console.log(data);
            if (data.requires2FA) {
                show2FAForm();
            } else {
                currentUsername = data.username;
                localStorage.setItem('melanCloudUser', currentUsername);
                showMain();
                checkAdminStatus();
            }
        } else {
            const error = await response.text();
            if (error.includes("заблокирован")) {
                alert(error);
            } else {
                alert(`Login failed: ${error}`);
            }
        }
    } catch (error) {
        console.error('Login error:', error);
        alert('An error occurred during login.');
    }
}

async function verify2FA() {
    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;
    const code = document.getElementById('twoFACode').value.trim();

    if (!code) {
        alert('Please enter verification code.');
        return;
    }

    try {
        const response = await fetch('/api/filemanager/login-with-2fa', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                Username: username, 
                Password: password,
                TwoFACode: code
            })
        });

        if (response.ok) {
            const data = await response.json();
            currentUsername = data.username;
            localStorage.setItem('melanCloudUser', currentUsername);
            showMain();
            checkAdminStatus();
        } else {
            const error = await response.text();
            alert(`2FA verification failed: ${error}`);
        }
    } catch (error) {
        console.error('2FA error:', error);
        alert('An error occurred during 2FA verification.');
    }
}

async function showRecycleBin() {
    try {
        const response = await fetch(`/api/filemanager/recycle-bin?username=${currentUsername}`);
        if (response.ok) {
            const recycleBin = await response.json();
            renderRecycledFiles(recycleBin.files);
            document.getElementById('retentionDays').value = recycleBin.retentionDays;
            showModal('recycleBinModal');
        }
    } catch (error) {
        console.error('Error loading recycle bin:', error);
    }
}

function renderRecycledFiles(files) {
    const container = document.getElementById('recycledFilesList');
    container.innerHTML = '';
    
    if (!files || files.length === 0) {
        container.innerHTML = '<div class="text-muted">Recycle bin is empty</div>';
        return;
    }
    
    files.forEach(file => {
        const fileCard = document.createElement('div');
        fileCard.className = 'file-card card';
        fileCard.innerHTML = `
            <div class="file-header">
                <i class="fas fa-file file-icon"></i>
                <div class="file-name">${file.originalPath}</div>
            </div>
            <div class="file-meta text-muted">
                <small>${formatFileSize(file.size)} • Deleted: ${new Date(file.deletedDate).toLocaleString()} • Space: ${file.spaceName}</small>
            </div>
            <div class="file-actions">
                <button class="action-btn" onclick="restoreFromRecycle('${file.recyclePath}')">
                    <i class="fas fa-trash-restore"></i> Restore
                </button>
                <button class="action-btn" onclick="permanentlyDelete('${file.recyclePath}')">
                    <i class="fas fa-trash"></i> Delete Permanently
                </button>
            </div>
        `;
        container.appendChild(fileCard);
    });
}

async function restoreFromRecycle(recycleFileName) {
    try {
        const response = await fetch(`/api/filemanager/restore-from-recycle?username=${currentUsername}&recycleFileName=${encodeURIComponent(recycleFileName)}`, {
            method: 'POST'
        });
        if (response.ok) {
            alert('File restored successfully');
            showRecycleBin();
            loadFiles();
        } else {
            const error = await response.text();
            alert(`Error restoring file: ${error}`);
        }
    } catch (error) {
        console.error('Error restoring file:', error);
        alert('Error restoring file');
    }
}

async function permanentlyDelete(recycleFileName) {
    if (confirm('Are you sure you want to permanently delete this file? This action cannot be undone.')) {
        try {
            const response = await fetch(`/api/filemanager/delete-from-recycle?username=${currentUsername}&recycleFileName=${encodeURIComponent(recycleFileName)}`, {
                method: 'POST'
            });
            if (response.ok) {
                alert('File permanently deleted');
                showRecycleBin();
            } else {
                const error = await response.text();
                alert(`Error deleting file: ${error}`);
            }
        } catch (error) {
            console.error('Error deleting file:', error);
            alert('Error deleting file');
        }
    }
}

async function emptyRecycleBin() {
    if (confirm('Are you sure you want to empty the recycle bin? All files will be permanently deleted.')) {
        try {
            const response = await fetch(`/api/filemanager/empty-recycle-bin?username=${currentUsername}`, {
                method: 'POST'
            });
            if (response.ok) {
                alert('Recycle bin emptied');
                closeModal('recycleBinModal');
            } else {
                const error = await response.text();
                alert(`Error emptying recycle bin: ${error}`);
            }
        } catch (error) {
            console.error('Error emptying recycle bin:', error);
            alert('Error emptying recycle bin');
        }
    }
}

async function updateRetentionDays() {
    const days = document.getElementById('retentionDays').value;
    try {
        const response = await fetch(`/api/filemanager/set-retention-days?username=${currentUsername}&days=${days}`, {
            method: 'POST'
        });
        if (!response.ok) {
            const error = await response.text();
            console.error('Error updating retention days:', error);
        }
    } catch (error) {
        console.error('Error updating retention days:', error);
    }
}

function show2FAModal() {
    fetch(`/api/filemanager/2fa-status?username=${currentUsername}`)
        .then(response => response.json())
        .then(data => {
            const statusContainer = document.getElementById('2faStatusContainer');
            const setupContainer = document.getElementById('2faSetupContainer');
            const statusText = document.getElementById('2faStatusText');
            const toggleButton = document.getElementById('toggle2FAButton');
            
            if (data.enabled) {
                statusText.textContent = 'enabled';
                toggleButton.innerHTML = '<i class="fas fa-shield-alt"></i> Disable 2FA';
                toggleButton.onclick = () => disable2FA();
                setupContainer.classList.add('hidden');
            } else {
                statusText.textContent = 'disabled';
                toggleButton.innerHTML = '<i class="fas fa-shield-alt"></i> Enable 2FA';
                toggleButton.onclick = () => enable2FA();
                setupContainer.classList.add('hidden');
            }
            document.getElementById('settingsModal').classList.add('hidden');
            showModal('twoFAModal');
        });
}

function enable2FA() {
    fetch(`/api/filemanager/enable-2fa?username=${currentUsername}`, {
        method: 'POST'
    })
    .then(response => response.json())
    .then(data => {
        document.getElementById('2faSecretKey').textContent = data.secretKey;
        document.getElementById('2faBackupCodes').textContent = data.backupCodes.join('\n');
        document.getElementById('2faStatusContainer').classList.add('hidden');
        document.getElementById('2faSetupContainer').classList.remove('hidden');
        
        document.getElementById('qrcode').innerHTML = '';
        new QRCode(document.getElementById('qrcode'), {
            text: `otpauth://totp/MelanCloud:${currentUsername}?secret=${data.secretKey}&issuer=MelanCloud`,
            width: 200,
            height: 200
        });
    });
}

function verify2FASetup() {
    const code = document.getElementById('2faVerifyCode').value.trim();
    if (!code) {
        alert('Please enter verification code');
        return;
    }
    
    fetch(`/api/filemanager/verify-2fa?username=${currentUsername}&code=${code}`, {
        method: 'POST'
    })
    .then(response => {
        if (response.ok) {
            alert('2FA enabled successfully');
            closeModal('twoFAModal');
        } else {
            return response.text().then(text => { throw new Error(text) });
        }
    })
    .catch(error => {
        alert(`Error enabling 2FA: ${error.message}`);
    });
}

function disable2FA() {
    if (confirm('Are you sure you want to disable two-factor authentication? This reduces your account security.')) {
        fetch(`/api/filemanager/disable-2fa?username=${currentUsername}`, {
            method: 'POST'
        })
        .then(response => {
            if (response.ok) {
                alert('2FA disabled successfully');
                closeModal('twoFAModal');
            } else {
                return response.text().then(text => { throw new Error(text) });
            }
        })
        .catch(error => {
            alert(`Error disabling 2FA: ${error.message}`);
        });
    }
}

async function register() {
    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;

    if (!username || !password) {
        alert('Please enter both username and password.');
        return;
    }

    if (username.length < 4 || username.length > 20) {
        alert('Username must be between 4 and 20 characters.');
        return;
    }

    if (password.length < 6) {
        alert('Password must be at least 6 characters.');
        return;
    }

    try {
        const response = await fetch('/api/filemanager/register', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                Username: username, 
                Password: password 
            })
        });

        if (response.ok) {
            alert('Registration successful! Please login.');
            document.getElementById('username').value = '';
            document.getElementById('password').value = '';
        } else {
            const error = await response.text();
            alert(`Registration failed: ${error}`);
        }
    } catch (error) {
        console.error('Registration error:', error);
        alert('An error occurred during registration.');
    }
}

function logout() {
    currentUsername = null;
    currentSpace = null;
    localStorage.removeItem('melanCloudUser');
    showAuth();
    showAuthForm()
    previewContainer.classList.add('hidden');
    fileList.innerHTML = '';
    currentSpaceElement.textContent = '[No Space Selected]';
}

async function loadUserInfo() {
    if (!currentUsername) return;

    try {
        const response = await fetch(`/api/filemanager/userinfo?username=${currentUsername}`);
        if (response.ok) {
            const data = await response.json();
            const limit = data.isPremium ? '6 GB' : '2 GB';
            const usedStorage = data.usedStorage || 0;
            const usedStorageGB = (usedStorage / 1_000_000_000).toFixed(2);
            
            userInfoElement.innerHTML = `
                <i class="fas fa-user"></i> ${currentUsername} 
                <span class="text-muted">•</span> 
                <i class="fas fa-database"></i> ${usedStorageGB} GB / ${limit}
                ${data.isPremium ? '<span class="text-muted">•</span> <i class="fas fa-crown text-primary"></i> Premium' : ''}
            `;
        } else {
            userInfoElement.innerHTML = '<i class="fas fa-exclamation-triangle"></i> Error loading user info';
        }
    } catch (error) {
        console.error('Error loading user info:', error);
        userInfoElement.innerHTML = '<i class="fas fa-exclamation-triangle"></i> Error loading user info';
    }
}

async function loadSpaces() {
    if (!currentUsername) {
        spaceList.innerHTML = '<div class="text-muted">Please login to view spaces.</div>';
        return;
    }

    try {
        const response = await fetch(`/api/filemanager/userinfo?username=${currentUsername}`);
        if (response.ok) {
            const data = await response.json();
            spaceList.innerHTML = '';

            if (!data.spaces || data.spaces.length === 0) {
                spaceList.innerHTML = '<div class="text-muted">No spaces available. Create one to get started.</div>';
                return;
            }

            data.spaces.forEach(space => {
                const spaceCard = document.createElement('div');
                spaceCard.className = `space-card card ${currentSpace === space.name ? 'active' : ''}`;
                spaceCard.innerHTML = `
                    <div class="space-name">
                        <i class="fas ${space.isPublic ? 'fa-globe' : 'fa-lock'}"></i>
                        ${space.name}
                    </div>
                    <div class="space-meta">
                        <span><i class="fas fa-hdd"></i> ${(space.usedStorage / 1_000_000_000).toFixed(2)} GB</span>
                        <span>${space.isPublic ? 'Public' : 'Private'}</span>
                    </div>
                    <div class="space-actions">
                        <button class="action-btn" onclick="switchSpace('${space.name}')">
                            <i class="fas fa-folder-open"></i> Open
                        </button>
                        <button class="action-btn" onclick="renameSpacePrompt('${space.name}')">
                            <i class="fas fa-edit"></i> Rename
                        </button>
                        <button class="action-btn" onclick="togglePublic('${space.name}', ${!space.isPublic})">
                            <i class="fas ${space.isPublic ? 'fa-lock' : 'fa-globe'}"></i> ${space.isPublic ? 'Make Private' : 'Make Public'}
                        </button>
                        ${space.name !== 'Private' ? `
                        <button class="action-btn" onclick="deleteSpacePrompt('${space.name}')">
                            <i class="fas fa-trash"></i> Delete
                        </button>
                        ` : ''}
                        <button class="action-btn" onclick="shareSpace('${space.name}')">
                            <i class="fas fa-share-alt"></i> Share
                        </button>
                    </div>
                `;
                spaceList.appendChild(spaceCard);
            });

            if (!currentSpace && data.spaces.length > 0) {
                switchSpace(data.spaces[0].name);
            }
        } else {
            spaceList.innerHTML = '<div class="text-muted">Error loading spaces.</div>';
        }
    } catch (error) {
        console.error('Error loading spaces:', error);
        spaceList.innerHTML = '<div class="text-muted">Error loading spaces.</div>';
    }
}

async function createSpace() {
    const spaceName = document.getElementById('newSpaceName').value.trim();
    const isPublic = document.getElementById('isPublic').checked;

    if (!spaceName) {
        alert('Space name cannot be empty.');
        return;
    }

    if (spaceName.length < 3) {
        alert('Space name must be at least 3 characters long.');
        return;
    }

    if (!/^[a-zA-Z0-9_-]+$/.test(spaceName)) {
        alert('Space name can only contain letters, numbers, underscores, or hyphens.');
        return;
    }

    try {
        const response = await fetch(`/api/filemanager/create-space?username=${currentUsername}&spaceName=${spaceName}&isPublic=${isPublic}`, {
            method: 'POST'
        });

        if (response.ok) {
            closeModal('createSpaceModal');
            loadSpaces();
        } else {
            const error = await response.text();
            alert(`Failed to create space: ${error}`);
        }
    } catch (error) {
        console.error('Error creating space:', error);
        alert('An error occurred while creating the space.');
    }
}

async function switchSpace(spaceName) {
    currentSpace = spaceName;
    currentSpaceElement.textContent = spaceName;
    previewContainer.classList.add('hidden');
    loadFiles();
}

async function loadFiles() {
    if (!currentSpace) {
        fileList.innerHTML = '<div class="text-muted">Please select a space first.</div>';
        return;
    }

    try {
        const response = await fetch(`/api/filemanager/files?username=${currentUsername}&spaceName=${currentSpace}`);
        if (response.ok) {
            const files = await response.json();
            selectedFileNames.clear();
            currentFiles = files;
            renderFileList(files);
        } else {
            fileList.innerHTML = '<div class="text-muted">Error loading files.</div>';
        }
    } catch (error) {
        fileList.innerHTML = '<div class="text-muted">Error loading files.</div>';
    }
}

function renderFileList(items) {
    fileList.innerHTML = '';
    if (!items || items.length === 0) {
        fileList.innerHTML = '<div class="text-muted">No files or folders in this space.</div>';
        return;
    }

    const fileTree = { files: [], folders: {} };
    
    items.forEach(item => {
        const parts = item.path.split('/');
        let current = fileTree;
        
        for (let i = 0; i < parts.length; i++) {
            const part = parts[i];
            if (i === parts.length - 1) {
                if (item.type === 'file') {
                    current.files.push(item);
                } else if (item.type === 'folder' && !current.folders[part]) {
                    current.folders[part] = { files: [], folders: {} };
                }
            } else {
                if (!current.folders[part]) {
                    current.folders[part] = { files: [], folders: {} };
                }
                current = current.folders[part];
            }
        }
    });

    function renderTree(tree, container, depth = 0) {
        Object.entries(tree.folders).sort().forEach(([folderName, subtree]) => {
            const folderDiv = document.createElement('div');
            folderDiv.className = 'file-card card';
            folderDiv.style.marginLeft = `${depth * 20}px`;
            
            const folderContent = document.createElement('div');
            folderContent.className = 'folder-content';
            folderContent.style.display = 'none';

            folderDiv.innerHTML = `
                <div class="file-header" style="cursor: pointer;" onclick="toggleFolderContent(this)">
                    <span class="folder-toggle">▶</span>
                    <i class="fas fa-folder file-icon"></i>
                    <div class="file-name">${folderName}</div>
                </div>
                <div class="file-actions">
                    <button class="action-btn" onclick="renameFolderPrompt('${folderName}')"><i class="fas fa-edit"></i> Rename</button>
                    <button class="action-btn" onclick="deleteFolderPrompt('${folderName}')"><i class="fas fa-trash"></i> Delete</button>
                </div>
            `;
            
            folderDiv.appendChild(folderContent);
            container.appendChild(folderDiv);
            
            renderTree(subtree, folderContent, depth + 1);
        });

        tree.files.sort((a, b) => a.name.localeCompare(b.name)).forEach(file => {
            const fileName = file.name;
            const fileSize = file.size || 0;
            const modifiedDate = file.modified ? new Date(file.modified).toLocaleString() : 'Unknown';
            const fileType = fileName.substring(fileName.lastIndexOf('.') + 1).toLowerCase();
            const isImage = ['jpg', 'jpeg', 'png', 'gif'].includes(fileType);
            const isArchive = ['zip', 'rar', '7z'].includes(fileType);

            const fileCard = document.createElement('div');
            fileCard.className = `file-card card ${selectedFileNames.has(file.path) ? 'selected' : ''}`;
            fileCard.style.marginLeft = `${depth * 20}px`;
            fileCard.dataset.filename = file.path;
            fileCard.dataset.size = fileSize;
            fileCard.dataset.modified = file.modified || '';
            fileCard.dataset.type = fileType;
            fileCard.innerHTML = `
                <div class="file-header">
                    ${isImage ? `<img src="/api/filemanager/preview?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(file.path)}" style="width: 50px; height: 50px; object-fit: cover; border-radius: 4px;">` : `<i class="fas ${getFileIcon(fileName)} file-icon"></i>`}
                    <div class="file-name" title="${file.path}">${fileName}</div>
                </div>
                <div class="file-meta text-muted">
                    <small>${formatFileSize(fileSize)} • ${modifiedDate} • ${file.isPublic ? 'Public' : 'Private'}</small>
                </div>
                <div class="file-actions">
                    ${isArchive ? `<button class="action-btn" onclick="extractArchive('${file.path}')"><i class="fas fa-file-export"></i> Extract</button>` : ''}
                    <button class="action-btn" onclick="previewFile('${file.path}')"><i class="fas fa-eye"></i> Preview</button>
                    <button class="action-btn" onclick="openMoveFileModal('${file.path}')"><i class="fas fa-arrow-right"></i> Move</button>
                    <button class="action-btn" onclick="downloadFile('${file.path}')"><i class="fas fa-download"></i> Download</button>
                    <button class="action-btn" onclick="deleteFilePrompt('${file.path}')"><i class="fas fa-trash"></i> Delete</button>
                    <button class="action-btn" onclick="renameFilePrompt('${file.path}')"><i class="fas fa-edit"></i> Rename</button>
                    <button class="action-btn" onclick="toggleFilePublic('${file.path}')"><i class="fas ${file.isPublic ? 'fa-lock' : 'fa-globe'}"></i> ${file.isPublic ? 'Make Private' : 'Make Public'}</button>
                </div>
            `;
            fileCard.addEventListener('click', (e) => {
                if (e.ctrlKey) toggleFileSelection(file.path, fileCard);
                else if (e.shiftKey && selectedFileNames.size > 0) selectRange(file.path);
                else { clearSelection(); toggleFileSelection(file.path, fileCard); }
                updateMultiActions();
            });
            container.appendChild(fileCard);
        });
    }

    renderTree(fileTree, fileList);
}

async function renameFolderPrompt(folderPath) {
    const newName = prompt('Enter new folder name:', folderPath);
    if (newName && newName !== folderPath) {
        try {
            const response = await fetch(`/api/filemanager/rename-folder?username=${currentUsername}&spaceName=${currentSpace}&oldFolderPath=${encodeURIComponent(folderPath)}&newFolderPath=${encodeURIComponent(newName)}`, {
                method: 'POST'
            });
            if (response.ok) {
                loadFiles();
            } else {
                const error = await response.text();
                alert(`Failed to rename folder: ${error}`);
            }
        } catch (error) {
            alert('Error renaming folder.');
        }
    }
}

async function deleteFolderPrompt(folderPath) {
    if (confirm(`Are you sure you want to delete the folder "${folderPath}" and all its contents?`)) {
        try {
            const response = await fetch(`/api/filemanager/delete-folder?username=${currentUsername}&spaceName=${currentSpace}&folderPath=${encodeURIComponent(folderPath)}`, {
                method: 'POST'
            });
            if (response.ok) {
                loadFiles();
                loadUserInfo();
            } else {
                const error = await response.text();
                alert(`Failed to delete folder: ${error}`);
            }
        } catch (error) {
            alert('Error deleting folder.');
        }
    }
}

function toggleFolderContent(element) {
    const folderDiv = element.closest('.file-card');
    const content = folderDiv.querySelector('.folder-content');
    const toggle = folderDiv.querySelector('.folder-toggle');
    
    if (content.style.display === 'none') {
        content.style.display = 'block';
        toggle.textContent = '▼';
    } else {
        content.style.display = 'none';
        toggle.textContent = '▶';
    }
}

async function extractArchive(filename) {
    if (confirm(`Extract "${filename}" into current space?`)) {
        try {
            const response = await fetch(`/api/filemanager/extract-archive?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`, {
                method: 'POST'
            });
            if (response.ok) {
                loadFiles();
            } else {
                const error = await response.text();
                alert(`Failed to extract archive: ${error}`);
            }
        } catch (error) {
            alert('Error extracting archive.');
        }
    }
}

async function toggleFilePublic(filename) {
    try {
        const response = await fetch(`/api/filemanager/toggle-file-public?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`, {
            method: 'POST'
        });
        if (response.ok) {
            alert('File public status toggled.');
        } else {
            const error = await response.text();
            alert(`Failed: ${error}`);
        }
    } catch (error) {
        alert('Error toggling file public status.');
    }
}

async function renameFilePrompt(filename) {
    const newName = prompt('Enter new file name:', filename);
    if (newName && newName !== filename) {
        try {
            const response = await fetch(`/api/filemanager/rename-file?username=${currentUsername}&spaceName=${currentSpace}&oldFileName=${encodeURIComponent(filename)}&newFileName=${encodeURIComponent(newName)}`, {
                method: 'POST'
            });
            if (response.ok) {
                loadFiles();
            } else {
                const error = await response.text();
                alert(`Failed to rename file: ${error}`);
            }
        } catch (error) {
            alert('Error renaming file.');
        }
    }
}

function toggleFileSelection(fileName, fileCard) {
    if (selectedFileNames.has(fileName)) {
        selectedFileNames.delete(fileName);
        fileCard.classList.remove('selected');
    } else {
        selectedFileNames.add(fileName);
        fileCard.classList.add('selected');
    }
}

function selectRange(endFileName) {
    const files = Array.from(fileList.children);
    const startIdx = files.findIndex(f => selectedFileNames.has(f.dataset.filename));
    const endIdx = files.findIndex(f => f.dataset.filename === endFileName);

    if (startIdx === -1 || endIdx === -1) return;

    const [from, to] = [Math.min(startIdx, endIdx), Math.max(startIdx, endIdx)];
    clearSelection();

    for (let i = from; i <= to; i++) {
        const fileName = files[i].dataset.filename;
        selectedFileNames.add(fileName);
        files[i].classList.add('selected');
    }
}

function clearSelection() {
    selectedFileNames.clear();
    document.querySelectorAll('.file-card').forEach(card => card.classList.remove('selected'));
}

function updateMultiActions() {
    multiActions.classList.toggle('active', selectedFileNames.size > 0);
}

async function filterFiles() {
    const query = searchInput.value.trim().toLowerCase();
    if (!currentSpace || !query) {
        loadFiles();
        return;
    }

    const searchInContent = searchContentCheckbox.checked;

    if (searchInContent) {
        try {
            const response = await fetch(`/api/filemanager/search-content?username=${currentUsername}&spaceName=${currentSpace}&query=${encodeURIComponent(query)}`);
            if (response.ok) {
                const files = await response.json();
                renderFileList(files);
            } else {
                fileList.innerHTML = '<div class="text-muted">Error searching files.</div>';
            }
        } catch (error) {
            console.error('Error searching content:', error);
            fileList.innerHTML = '<div class="text-muted">Error searching files.</div>';
        }
    } else {
        const filteredFiles = currentFiles.filter(file => {
            const fileName = file.name.toLowerCase();
            const fileType = fileName.substring(fileName.lastIndexOf('.') + 1).toLowerCase();
            return fileName.includes(query) || fileType.includes(query);
        });
        renderFileList(filteredFiles);
    }
}

function sortFiles(key, direction) {
    const sortedFiles = [...currentFiles];
    sortedFiles.sort((a, b) => {
        let valA, valB;
        switch (key) {
            case 'name':
                valA = a.name.toLowerCase();
                valB = b.name.toLowerCase();
                break;
            case 'modified':
                valA = new Date(a.modified || 0).getTime();
                valB = new Date(b.modified || 0).getTime();
                break;
            case 'size':
                valA = a.size || 0;
                valB = b.size || 0;
                break;
            case 'type':
                valA = (a.name.substring(a.name.lastIndexOf('.') + 1) || '').toLowerCase();
                valB = (b.name.substring(b.name.lastIndexOf('.') + 1) || '').toLowerCase();
                break;
        }
        if (direction === 'asc') {
            return valA < valB ? -1 : valA > valB ? 1 : 0;
        } else {
            return valA > valB ? -1 : valA < valB ? 1 : 0;
        }
    });
    renderFileList(sortedFiles);
}

async function deleteSelectedFiles() {
    if (selectedFileNames.size === 0) {
        alert('No files selected.');
        return;
    }

    if (confirm(`Are you sure you want to delete ${selectedFileNames.size} selected file(s)?`)) {
        try {
            const deletePromises = Array.from(selectedFileNames).map(filename =>
                fetch(`/api/filemanager/delete-file?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`, {
                    method: 'POST'
                }).then(response => {
                    if (!response.ok) throw new Error(`Failed to delete ${filename}`);
                    return filename;
                })
            );

            await Promise.all(deletePromises);
            selectedFileNames.clear();
            loadFiles();
            loadUserInfo();
            alert('Selected files deleted successfully.');
        } catch (error) {
            console.error('Error deleting selected files:', error);
            alert('An error occurred while deleting the selected files.');
        }
    }
}

async function downloadSelectedFiles() {
    if (selectedFileNames.size === 0) {
        alert('No files selected.');
        return;
    }

    if (selectedFileNames.size === 1) {
        downloadFile(Array.from(selectedFileNames)[0]);
        return;
    }

    try {
        const tempSpaceName = `temp_${Date.now()}`;
        await fetch(`/api/filemanager/create-space?username=${currentUsername}&spaceName=${tempSpaceName}&isPublic=false`, {
            method: 'POST'
        });

        const copyPromises = Array.from(selectedFileNames).map(filename => {
            const filePath = `${UserStore.GetUserSpaceDirectory(currentUsername, currentSpace)}/${filename}`;
            const tempPath = `${UserStore.GetUserSpaceDirectory(currentUsername, tempSpaceName)}/${filename}`;
            return fetch('/api/filemanager/copy-file', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    username: currentUsername,
                    fromSpace: currentSpace,
                    toSpace: tempSpaceName,
                    filename: filename
                })
            });
        });

        await Promise.all(copyPromises);
        window.open(`/api/filemanager/download-all?username=${currentUsername}&spaceName=${tempSpaceName}`, '_blank');
        await fetch(`/api/filemanager/delete-space?username=${currentUsername}&spaceName=${tempSpaceName}`, { method: 'POST' });
        selectedFileNames.clear();
        loadFiles();
    } catch (error) {
        console.error('Error downloading selected files:', error);
        alert('An error occurred while downloading the selected files.');
    }
}

async function moveSelectedFilesPrompt() {
    if (selectedFileNames.size === 0) {
        alert('No files selected.');
        return;
    }

    const spacesResponse = await fetch(`/api/filemanager/userinfo?username=${currentUsername}`);
    const spacesData = await spacesResponse.json();
    const availableSpaces = spacesData.spaces.filter(s => s.name !== currentSpace).map(s => s.name);
    if (availableSpaces.length === 0) {
        alert('No other spaces available to move files to.');
        return;
    }

    const targetSpace = prompt(`Enter the space to move ${selectedFileNames.size} file(s) to:\nAvailable spaces: ${availableSpaces.join(', ')}`);
    if (!targetSpace || !availableSpaces.includes(targetSpace)) {
        alert('Invalid space name.');
        return;
    }

    try {
        const movePromises = Array.from(selectedFileNames).map(filename =>
            fetch('/api/filemanager/move-file', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    username: currentUsername,
                    fromSpace: currentSpace,
                    toSpace: targetSpace,
                    filename: filename
                })
            }).then(response => {
                if (!response.ok) throw new Error(`Failed to move ${filename}`);
                return filename;
            })
        );

        await Promise.all(movePromises);
        selectedFileNames.clear();
        loadFiles();
        loadUserInfo();
        alert(`Selected files moved to ${targetSpace} successfully.`);
    } catch (error) {
        console.error('Error moving selected files:', error);
        alert('An error occurred while moving the selected files.');
    }
}

async function uploadFiles() {
    if (uploadInProgress) {
        alert('Upload already in progress. Please wait for completion or cancel the current upload.');
        return;
    }

    if (!currentSpace) {
        alert('Please select a space for uploading.');
        return;
    }

    if (selectedFiles.length === 0) {
        alert('Please select at least one file to upload.');
        return;
    }

    const canUpload = await checkStorageLimit(selectedFiles);
    if (!canUpload) return;

    uploadInProgress = true;
    uploadBtn.disabled = true;
    cancelUploadBtn.style.display = 'inline-flex';
    
    uploadProgress.value = 0;
    uploadProgressContainer.classList.remove('hidden');
    uploadStatus.textContent = `Preparing to upload ${selectedFiles.length} file(s)...`;
    
    selectedFiles.forEach((_, index) => {
        const progressBar = document.getElementById(`fileProgress${index}`);
        if (progressBar) progressBar.value = 0;
    });

    uploadController = new AbortController();
    currentUploadIndex = 0;
    
    try {
        for (let i = 0; i < selectedFiles.length; i++) {
            currentUploadIndex = i;
            const file = selectedFiles[i];
            
            currentUploadFileElement.textContent = file.name;
            uploadPercentageElement.textContent = '0%';
            
            const formData = new FormData();
            formData.append('file', file, transliterate(file.name));
            
            const xhr = new XMLHttpRequest();
            xhr.open('POST', `/api/filemanager/upload?username=${currentUsername}&spaceName=${currentSpace}`, true);
            
            xhr.upload.onprogress = (e) => {
                if (e.lengthComputable) {
                    const percent = Math.round((e.loaded / e.total) * 100);
                    document.getElementById(`fileProgress${i}`).value = percent;
                    
                    let totalLoaded = 0;
                    let totalSize = selectedFiles.reduce((sum, f) => sum + f.size, 0);
                    
                    for (let j = 0; j < i; j++) {
                        totalLoaded += selectedFiles[j].size;
                    }
                    totalLoaded += (file.size * percent / 100);
                    
                    const overallPercent = Math.round((totalLoaded / totalSize) * 100);
                    uploadProgress.value = overallPercent;
                    uploadPercentageElement.textContent = `${overallPercent}%`;
                }
            };
            
            await new Promise((resolve, reject) => {
                xhr.onload = () => {
                    if (xhr.status === 200) {
                        resolve();
                    } else {
                        reject(new Error(xhr.responseText || 'Error loading file'));
                    }
                };
                
                xhr.onerror = () => {
                    reject(new Error('Network error'));
                };
                
                xhr.onabort = () => {
                    reject(new Error('Upload cancelled'));
                };
                
                xhr.send(formData);
            });
        }
        
        uploadStatus.textContent = 'Upload completed successfully!';
        
        setTimeout(async () => {
            uploadProgressContainer.classList.add('hidden');
            await loadFiles();
            loadUserInfo();
            
            resetUploadState();
            selectedFiles = [];
            fileInput.value = '';
            document.getElementById('selectedFiles').classList.add('hidden');
        }, 1500);
        
    } catch (error) {
        console.error('Ошибка загрузки:', error);
        
        if (error.name === 'AbortError') {
            uploadStatus.textContent = 'Загрузка отменена';
        } else {
            uploadStatus.textContent = `Ошибка загрузки: ${error.message}`;
            
            if (currentUploadIndex < selectedFiles.length) {
                const failedFile = selectedFiles[currentUploadIndex];
                uploadStatus.textContent += ` (Ошибка при загрузке: ${failedFile.name})`;
            }
        }
        
        resetUploadState();
    }
}

function resetUploadState() {
    uploadInProgress = false;
    uploadBtn.disabled = false;
    cancelUploadBtn.style.display = 'none';
    if (uploadController) uploadController.abort();
}

function cancelUpload() {
    if (uploadInProgress && uploadController) {
        uploadController.abort();
        resetUploadState();
        uploadStatus.textContent = 'Upload cancelled';
    }
}

function downloadFile(filename) {
    window.open(`/api/filemanager/download?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`, '_blank');
}

async function previewFile(filename) {
    const extension = filename.substring(filename.lastIndexOf('.')).toLowerCase();
    previewFileName.textContent = filename;
    previewContent.innerHTML = '<div class="text-muted"><i class="fas fa-spinner fa-spin"></i> Loading preview...</div>';
    previewContainer.classList.remove('hidden');

    if (['.docx'].includes(extension)) {
        try {
            const response = await fetch(`/api/filemanager/download?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`);
            const arrayBuffer = await response.arrayBuffer();
            const result = await mammoth.convertToHtml({ arrayBuffer });
            previewContent.innerHTML = `<div style="max-height: 70vh; overflow-y: auto;">${result.value}</div>`;
        } catch (error) {
            previewContent.innerHTML = '<div class="text-muted">Error loading DOCX preview.</div>';
        }
    }
    else if (['.jpg', '.jpeg', '.png'].includes(extension)) {
        const img = document.createElement('img');
        img.src = `/api/filemanager/preview?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`;
        previewContent.innerHTML = '';
        previewContent.appendChild(img);
        img.onload = () => {
            EXIF.getData(img, function() {
                const exifData = EXIF.pretty(this);
                if (exifData) {
                    previewContent.innerHTML += `<pre>${exifData}</pre>`;
                }
            });
        };
    }
    else if (['.mp4', '.webm', '.mov'].includes(extension)) {
        previewContent.innerHTML = `
            <video controls autoplay>
                <source src="/api/filemanager/preview?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}" type="video/${extension.substring(1)}">
                Your browser does not support the video tag.
            </video>
        `;
    } 
    else if (['.mp3', '.wav', '.flac'].includes(extension)) {
        previewContent.innerHTML = `
            <audio controls autoplay>
                <source src="/api/filemanager/preview?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}" type="audio/${extension.substring(1)}">
                Your browser does not support the audio element.
            </audio>
        `;
    } 
    else if (['.txt', '.json', '.xml', '.csv'].includes(extension)) {
        try {
            const response = await fetch(`/api/filemanager/preview?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`);
            if (response.ok) {
                const text = await response.text();
                previewContent.innerHTML = `
                    <textarea id="editTextContent" style="width: 100%; min-height: 70vh; background: #000; color: #fff; padding: 15px; border-radius: 6px;">${text}</textarea>
                    <button class="btn mt-20" onclick="saveTextFile('${filename}')"><i class="fas fa-save"></i> Save</button>
                `;
            } else {
                previewContent.innerHTML = '<div class="text-muted">Error loading text file.</div>';
            }
        } catch (error) {
            previewContent.innerHTML = '<div class="text-muted">Error loading text file.</div>';
        }
    }
    else if (extension === '.pdf') {
        previewContent.innerHTML = `
            <iframe src="/api/filemanager/preview?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}" 
                    width="100%" height="100%" style="min-height: 70vh;">
                Your browser does not support PDF preview.
            </iframe>
        `;
    }
    else if (extension === '.zip') {
        previewContent.innerHTML = '<div class="text-muted"><i class="fas fa-spinner fa-spin"></i> Loading archive contents...</div>';
        try {
            console.log(`Fetching ZIP file: /api/filemanager/download?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`);
            const response = await fetch(`/api/filemanager/download?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`);
            if (response.ok) {
                console.log("ZIP file fetched successfully");
                const blob = await response.blob();
                console.log("Loading ZIP into JSZip...");
                const zip = await JSZip.loadAsync(blob);
                const archiveList = document.createElement('div');
                archiveList.className = 'archive-list';
                await displayZipContents(zip, archiveList, filename, 0);
                previewContent.innerHTML = '';
                previewContent.appendChild(archiveList);
            } else {
                const error = await response.text();
                console.error("Failed to fetch ZIP:", error);
                previewContent.innerHTML = `<div class="text-muted">Error loading archive: ${error}</div>`;
            }
        } catch (error) {
            console.error('Error loading ZIP:', error);
            previewContent.innerHTML = `<div class="text-muted">Error loading archive: ${error.message}</div>`;
        }
    }
    else {
        previewContent.innerHTML = '<div class="text-muted">Preview not available for this file type.</div>';
    }
}

async function saveTextFile(filename) {
    const content = document.getElementById('editTextContent').value;
    try {
        const response = await fetch(`/api/filemanager/save-text-file?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(content)
        });
        if (response.ok) {
            alert('File saved successfully.');
        } else {
            const error = await response.text();
            alert(`Failed to save file: ${error}`);
        }
    } catch (error) {
        alert('Error saving file.');
    }
}

async function displayZipContents(zip, container, zipFilename, depth) {
    const files = [];
    const folders = {};

    zip.forEach((relativePath, zipEntry) => {
        const parts = relativePath.split('/');
        const isDir = zipEntry.dir || relativePath.endsWith('/');
        const name = parts[0];

        if (parts.length === 1) {
            if (!isDir) {
                files.push({ 
                    path: relativePath, 
                    entry: zipEntry,
                    size: zipEntry._data ? zipEntry._data.uncompressedSize : zipEntry.uncompressedSize,
                    modified: zipEntry.date
                });
            }
        } else {
            if (!folders[name]) {
                folders[name] = [];
            }
            const remainingPath = parts.slice(1).join('/');
            folders[name].push({ path: remainingPath, entry: zipEntry });
        }
    });

    files.sort((a, b) => a.path.localeCompare(b.path));
    const sortedFolders = Object.keys(folders).sort();

    for (const file of files) {
        const item = document.createElement('div');
        item.className = `archive-item ${depth > 0 ? 'nested' : ''}`;
        const extension = file.path.substring(file.path.lastIndexOf('.')).toLowerCase();
        const icon = getFileIcon(file.path);
        const size = file.size || 0;
        const modifiedDate = file.modified ? new Date(file.modified).toLocaleString() : 'Unknown';

        item.innerHTML = `
            <i class="fas ${icon}"></i>
            <span>${file.path}</span>
            <span class="archive-details">${formatFileSize(size)} • ${modifiedDate}</span>
            <button class="action-btn" onclick="downloadFromZip('${zipFilename}', '${file.path}')">
                <i class="fas fa-download"></i>
            </button>
            ${extension === '.zip' ? `
            <button class="action-btn" onclick="previewNestedZip('${zipFilename}', '${file.path}')">
                <i class="fas fa-eye"></i>
            </button>
            ` : ''}
        `;
        container.appendChild(item);
    }

    for (const folderName of sortedFolders) {
        const folderDiv = document.createElement('div');
        folderDiv.className = `archive-item ${depth > 0 ? 'nested' : ''}`;
        const folderContent = document.createElement('div');
        folderContent.className = 'archive-list';
        folderContent.style.display = 'none';

        folderDiv.innerHTML = `
            <span class="folder-toggle" onclick="toggleFolder(this)">▶</span>
            <i class="fas fa-folder"></i>
            <span>${folderName}</span>
        `;
        folderDiv.appendChild(folderContent);
        container.appendChild(folderDiv);

        const folderZip = new JSZip();
        for (const { path, entry } of folders[folderName]) {
            folderZip.file(path, entry.async('arraybuffer'));
        }
        await displayZipContents(folderZip, folderContent, zipFilename, depth + 1);
    }
}

function toggleFolder(element) {
    const content = element.parentElement.querySelector('.archive-list');
    if (content.style.display === 'none') {
        content.style.display = 'block';
        element.textContent = '▼';
    } else {
        content.style.display = 'none';
        element.textContent = '▶';
    }
}

async function previewNestedZip(zipFilename, relativePath) {
    try {
        const response = await fetch(`/api/filemanager/download?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(zipFilename)}`);
        if (response.ok) {
            const blob = await response.blob();
            const zip = await JSZip.loadAsync(blob);
            const nestedZipBlob = await zip.file(relativePath).async('blob');
            const nestedZip = await JSZip.loadAsync(nestedZipBlob);

            previewFileName.textContent = `${zipFilename} > ${relativePath}`;
            const archiveList = document.createElement('div');
            archiveList.className = 'archive-list';
            await displayZipContents(nestedZip, archiveList, zipFilename, 0);
            previewContent.innerHTML = '';
            previewContent.appendChild(archiveList);
        }
    } catch (error) {
        previewContent.innerHTML = '<div class="text-muted">Error loading nested archive.</div>';
    }
}

async function downloadFromZip(zipFilename, relativePath) {
    try {
        const response = await fetch(`/api/filemanager/download?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(zipFilename)}`);
        if (response.ok) {
            const blob = await response.blob();
            const zip = await JSZip.loadAsync(blob);
            const fileBlob = await zip.file(relativePath).async('blob');
            const url = URL.createObjectURL(fileBlob);
            const a = document.createElement('a');
            a.href = url;
            a.download = relativePath.split('/').pop() || relativePath;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }
    } catch (error) {
        console.error('Error downloading from ZIP:', error);
        alert('Error downloading file from archive.');
    }
}

function closePreview() {
    previewContainer.classList.add('hidden');
}

function deleteFilePrompt(filename) {
    if (confirm(`Move "${filename}" to recycle bin? You can restore it later if needed.`)) {
        fetch(`/api/filemanager/delete-to-recycle?username=${currentUsername}&spaceName=${currentSpace}&filename=${encodeURIComponent(filename)}`, {
            method: 'POST'
        })
        .then(response => {
            if (response.ok) {
                loadFiles();
                loadUserInfo();
                closePreview();
            } else {
                return response.text().then(text => { throw new Error(text) });
            }
        })
        .catch(error => {
            alert(`Error moving file to recycle bin: ${error.message}`);
        });
    }
}

async function renameSpacePrompt(oldName) {
    const newName = prompt('Enter new space name:', oldName);
    if (newName && newName !== oldName) {
        try {
            const response = await fetch(`/api/filemanager/rename-space?username=${currentUsername}&oldSpaceName=${oldName}&newSpaceName=${newName}`, {
                method: 'POST'
            });
            if (response.ok) {
                if (currentSpace === oldName) currentSpace = newName;
                loadSpaces();
            } else {
                const error = await response.text();
                alert(`Failed to rename space: ${error}`);
            }
        } catch (error) {
            console.error('Error renaming space:', error);
            alert('An error occurred while renaming the space.');
        }
    }
}

async function togglePublic(spaceName, makePublic) {
    try {
        const response = await fetch(`/api/filemanager/toggle-space-public?username=${currentUsername}&spaceName=${spaceName}&isPublic=${makePublic}`, {
            method: 'POST'
        });
        if (response.ok) {
            loadSpaces();
        } else {
            const error = await response.text();
            alert(`Failed to update space: ${error}`);
        }
    } catch (error) {
        console.error('Error toggling space public status:', error);
        alert('An error occurred while updating the space.');
    }
}

async function deleteSpacePrompt(spaceName) {
    if (confirm(`Are you sure you want to delete the space "${spaceName}"? All files in this space will be permanently deleted.`)) {
        try {
            const response = await fetch(`/api/filemanager/delete-space?username=${currentUsername}&spaceName=${spaceName}`, {
                method: 'POST'
            });
            if (response.ok) {
                if (currentSpace === spaceName) currentSpace = null;
                loadSpaces();
                if (!currentSpace) fileList.innerHTML = '';
                closePreview();
            } else {
                const error = await response.text();
                alert(`Failed to delete space: ${error}`);
            }
        } catch (error) {
            console.error('Error deleting space:', error);
            alert('An error occurred while deleting the space.');
        }
    }
}

async function shareSpace(spaceName) {
    try {
        const response = await fetch(`/api/filemanager/share-link?username=${currentUsername}&spaceName=${spaceName}`);
        if (response.ok) {
            const data = await response.json();
            if (data && data.link) {
                prompt('Share this link with others:', data.link);
            } else {
                alert('Error: No share link received from server.');
            }
        } else {
            const error = await response.text();
            alert(`Error creating share link: ${error}`);
        }
    } catch (error) {
        console.error('Error sharing space:', error);
        alert('An error occurred while trying to share the space.');
    }
}


fileInput.addEventListener('change', async (e) => {
    if (e.target.files.length > 0) {
        const newFiles = Array.from(e.target.files);
        

        const canAddFiles = await checkStorageLimit(newFiles);
        if (!canAddFiles) {
            fileInput.value = ''; 
            return;
        }


        newFiles.forEach(newFile => {
            const isDuplicate = selectedFiles.some(
                existingFile => existingFile.name === newFile.name && 
                            existingFile.size === newFile.size &&
                            existingFile.lastModified === newFile.lastModified
            );
            
            if (!isDuplicate) {
                selectedFiles.push(newFile);
            }
        });
        
        const dataTransfer = new DataTransfer();
        selectedFiles.forEach(file => dataTransfer.items.add(file));
        fileInput.files = dataTransfer.files;
        
        updateSelectedFilesList();
    }
});

async function checkStorageLimit(filesToAdd = []) {
    try {
        const response = await fetch(`/api/filemanager/userinfo?username=${currentUsername}`);
        if (response.ok) {
            const data = await response.json();
            const isPremium = data.isPremium || false;
            const limitBytes = isPremium ? 6 * 1024 * 1024 * 1024 : 2 * 1024 * 1024 * 1024;
            const usedStorage = data.usedStorage || 0;
            
            const newFilesSize = filesToAdd.reduce((total, file) => total + file.size, 0);
            const totalAfterUpload = usedStorage + newFilesSize;
            
            if (totalAfterUpload > limitBytes) {
                const remainingBytes = limitBytes - usedStorage;
                const remainingFormatted = formatFileSize(remainingBytes);
                const limitFormatted = isPremium ? '6 GB' : '2 GB';
                
                alert(`Storage limit exceeded!\n\nAvailable: ${remainingFormatted} of ${limitFormatted}\nDelete files or upgrade to Premium.`);
                return false;
            }
            return true;
        }
    } catch (error) {
        alert('Failed to check storage limit.');
        return false;
    }
    return false;
}

async function copySelectedFilesPrompt() {
    if (selectedFileNames.size === 0) {
        alert('No files selected.');
        return;
    }

    const spacesResponse = await fetch(`/api/filemanager/userinfo?username=${currentUsername}`);
    const spacesData = await spacesResponse.json();
    const availableSpaces = spacesData.spaces.filter(s => s.name !== currentSpace).map(s => s.name);
    if (availableSpaces.length === 0) {
        alert('No other spaces available to copy files to.');
        return;
    }

    const targetSpace = prompt(`Enter the space to copy ${selectedFileNames.size} file(s) to:\nAvailable spaces: ${availableSpaces.join(', ')}`);
    if (!targetSpace || !availableSpaces.includes(targetSpace)) {
        alert('Invalid space name.');
        return;
    }

    try {
        const copyPromises = Array.from(selectedFileNames).map(filename =>
            fetch('/api/filemanager/copy-file', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    username: currentUsername,
                    fromSpace: currentSpace,
                    toSpace: targetSpace,
                    filename: filename
                })
            }).then(response => {
                if (!response.ok) throw new Error(`Failed to copy ${filename}`);
                return filename;
            })
        );

        await Promise.all(copyPromises);
        alert(`Selected files copied to ${targetSpace} successfully.`);
    } catch (error) {
        alert('Error copying selected files.');
    }
}

async function createFolderPrompt() {
    const folderName = prompt('Enter folder name or path (e.g., folder/subfolder):');
    if (folderName) {
        try {
            const response = await fetch(`/api/filemanager/create-folder?username=${currentUsername}&spaceName=${currentSpace}&folderPath=${encodeURIComponent(folderName)}`, {
                method: 'POST'
            });
            if (response.ok) {
                loadFiles();
            } else {
                const error = await response.text();
                alert(`Failed to create folder: ${error}`);
            }
        } catch (error) {
            alert('Error creating folder.');
        }
    }
}

function openMoveFileModal(filePath) {
    const modal = document.getElementById('moveFileModal');
    const fileNameSpan = document.getElementById('moveFileName');
    const targetSpaceSelect = document.getElementById('targetSpace');
    const targetLocationSelect = document.getElementById('targetLocation');

    fileNameSpan.textContent = filePath;
    modal.dataset.filePath = filePath;

    targetSpaceSelect.innerHTML = '';
    fetch(`/api/filemanager/userinfo?username=${currentUsername}`)
        .then(response => response.json())
        .then(user => {
            user.spaces.forEach(space => {
                const option = document.createElement('option');
                option.value = space.name;
                option.textContent = space.name;
                if (space.name === currentSpace) option.selected = true;
                targetSpaceSelect.appendChild(option);
            });
            updateTargetFolders();
        })
        .catch(error => console.error('Error loading spaces:', error));

    modal.classList.remove('hidden');
}

function closeMoveFileModal() {
    const modal = document.getElementById('moveFileModal');
    modal.classList.add('hidden');  
}

function updateTargetFolders() {
    const targetSpaceSelect = document.getElementById('targetSpace');
    const targetLocationSelect = document.getElementById('targetLocation');
    const selectedSpace = targetSpaceSelect.value;

    targetLocationSelect.innerHTML = '<option value="">Space Root</option>';

    fetch(`/api/filemanager/files?username=${currentUsername}&spaceName=${selectedSpace}`)
        .then(response => response.json())
        .then(items => {
            const folders = items.filter(item => item.type === 'folder');
            folders.forEach(folder => {
                const option = document.createElement('option');
                option.value = folder.path;
                option.textContent = folder.path;
                targetLocationSelect.appendChild(option);
            });
        })
        .catch(error => console.error('Error loading folders:', error));
}

async function confirmMoveFile() {
    const modal = document.getElementById('moveFileModal');
    const oldFilePath = modal.dataset.filePath;
    const targetSpace = document.getElementById('targetSpace').value;
    const targetLocation = document.getElementById('targetLocation').value;
    const fileName = oldFilePath.split('/').pop();
    const newFilePath = targetLocation ? `${targetLocation}/${fileName}` : fileName;

    const request = {
        username: currentUsername,
        fromSpace: currentSpace,
        filename: fileName,
        toSpace: targetSpace,
        oldFilePath: oldFilePath,
        newFilePath: newFilePath
    };

    try {
        const response = await fetch('/api/filemanager/move-file', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });
        if (response.ok) {
            closeMoveFileModal();
            loadFiles();
            loadUserInfo();
        } else {
            const error = await response.text();
            alert(`Failed to move file: ${error}`);
        }
    } catch (error) {
        alert('Error moving file.');
    }
}
async function loadPlugins() {
    try {
        const response = await fetch('/api/plugins');
        if (response.ok) {
            const plugins = await response.json();
            const pluginList = document.getElementById('pluginList');
            pluginList.innerHTML = '';
            
            plugins.forEach(plugin => {
                const pluginItem = document.createElement('div');
                pluginItem.className = 'space-card card';
                pluginItem.innerHTML = `
                    <div class="space-name">${plugin}</div>
                    <div class="space-actions">
                        <button class="action-btn" onclick="executePlugin('${plugin}')">
                            <i class="fas fa-play"></i> Execute
                        </button>
                    </div>
                `;
                pluginList.appendChild(pluginItem);
            });
        }
    } catch (error) {
        console.error('Error loading plugins:', error);
    }
}

function showPluginModal() {
    showModal('pluginModal');
}

async function uploadPlugin() {
    const fileInput = document.getElementById('pluginFileInput');
    if (fileInput.files.length === 0) {
        alert('Please select a plugin file');
        return;
    }

    const formData = new FormData();
    formData.append('file', fileInput.files[0]);

    try {
        const response = await fetch('/api/plugins/load', {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            alert('Plugin loaded successfully');
            loadPlugins();
            closeModal('pluginModal');
        } else {
            const error = await response.text();
            alert(`Error loading plugin: ${error}`);
        }
    } catch (error) {
        console.error('Error uploading plugin:', error);
        alert('Error uploading plugin');
    }
}

async function executePlugin(pluginName) {
    if (!currentSpace) {
        alert('Please select a space first');
        return;
    }

    try {
        const response = await fetch(`/api/plugins/execute/${pluginName}?username=${currentUsername}&spaceName=${currentSpace}`);
        if (response.ok) {
            const result = await response.json();
            alert(`Plugin executed successfully: ${JSON.stringify(result)}`);
        } else {
            const error = await response.text();
            alert(`Error executing plugin: ${error}`);
        }
    } catch (error) {
        console.error('Error executing plugin:', error);
        alert('Error executing plugin');
    }
}

async function showNotificationSettings() {
    try {
        const response = await fetch(`/api/filemanager/notification-settings?username=${currentUsername}`);
        if (response.ok) {
            const settings = await response.json();
            document.getElementById('emailNotifications').checked = settings.emailNotifications;
            document.getElementById('browserNotifications').checked = settings.browserNotifications;
            document.getElementById('onUpload').checked = settings.onUpload;
            document.getElementById('onDownload').checked = settings.onDownload;
            document.getElementById('onSpaceFull').checked = settings.onSpaceFull;
            document.getElementById('settingsModal').classList.add('hidden');
            showModal('notificationSettingsModal');
        }
    } catch (error) {
        console.error('Error loading notification settings:', error);
    }
}

async function saveNotificationSettings() {
    const settings = {
        emailNotifications: document.getElementById('emailNotifications').checked,
        browserNotifications: document.getElementById('browserNotifications').checked,
        onUpload: document.getElementById('onUpload').checked,
        onDownload: document.getElementById('onDownload').checked,
        onSpaceFull: document.getElementById('onSpaceFull').checked
    };

    try {
        const response = await fetch(`/api/filemanager/notification-settings?username=${currentUsername}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });

        if (response.ok) {
            closeModal('notificationSettingsModal');
            alert('Notification settings saved');
        } else {
            const error = await response.text();
            alert(`Error saving settings: ${error}`);
        }
    } catch (error) {
        console.error('Error saving notification settings:', error);
        alert('Error saving notification settings');
    }
}
function checkAdminStatus() {
    if (!currentUsername) return;
    
    fetch(`/api/filemanager/userinfo?username=${currentUsername}`)
        .then(response => response.json())
        .then(data => {
            const adminBtn = document.getElementById('adminPanelBtn');
            if (data.isAdmin) {
                console.log('Admin status:', data.isAdmin);
                adminBtn.classList.remove('hidden');
            } else {
                console.log('Not admin status:', data.isAdmin);
                adminBtn.classList.add('hidden');
            }
        });
}
function showAdminPanel() {
    loadAdminUsers();
    showModal('adminPanelModal');
}
async function loadAdminUsers() {
    const container = document.getElementById('adminUsersList');
    container.innerHTML = '<tr><td colspan="6" style="text-align: center; padding: 20px;"><i class="fas fa-spinner fa-spin"></i> Loading users...</td></tr>';
    
    try {
        const response = await fetch(`/api/filemanager/admin/users?adminUsername=${currentUsername}`);
        const users = await response.json();
        
        container.innerHTML = '';
        
        users.forEach(user => {
            const row = document.createElement('tr');
            
            const statusClass = user.isBlocked ? 'status-blocked' : 'status-active';
            const statusText = user.isBlocked ? 
                (user.blockedUntil ? `Blocked until ${new Date(user.blockedUntil).toLocaleDateString()}` : 'Blocked') : 
                'Active';
            
            const adminBadge = user.isAdmin ? 
                '<span class="admin-badge"><i class="fas fa-user-shield"></i> Admin</span>' : '';
            
            const premiumBadge = user.isPremium ? 
                '<span class="premium-badge"><i class="fas fa-crown"></i> Premium</span>' : '';
            
            row.innerHTML = `
                <td>${user.username}</td>
                <td>${premiumBadge}</td>
                <td>${adminBadge}</td>
                <td><span class="status-badge ${statusClass}">${statusText}</span></td>
                <td>${formatFileSize(user.totalStorage)} / ${user.isPremium ? '6 GB' : '2 GB'}</td>
                <td>
                    ${!user.isAdmin ? `
                    <button class="action-btn" onclick="toggleAdminStatus('${user.username}', ${!user.isAdmin})">
                        <i class="fas fa-user-shield"></i> ${user.isAdmin ? 'Revoke Admin' : 'Make Admin'}
                    </button>
                    ` : 
                    `
                    <button class="action-btn" onclick="toggleAdminStatus('${user.username}', ${!user.isAdmin})">
                        <i class="fas fa-user-shield"></i> ${user.isAdmin ? 'Revoke Admin' : 'Make Admin'}
                    </button>
                    `}
                    
                    <button class="action-btn" onclick="togglePremiumStatus('${user.username}', ${!user.isPremium})">
                        <i class="fas ${user.isPremium ? 'fa-crown' : 'fa-crown text-muted'}"></i> ${user.isPremium ? 'Remove Premium' : 'Add Premium'}
                    </button>
                    
                    ${user.isBlocked ? `
                    <button class="action-btn" onclick="unblockUser('${user.username}')">
                        <i class="fas fa-unlock"></i> Unblock
                    </button>
                    ` : `
                    <button class="action-btn" onclick="showBlockForm('${user.username}')">
                        <i class="fas fa-lock"></i> Block
                    </button>
                    `}
                </td>
            `;
            container.appendChild(row);
        });
    } catch (error) {
        container.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--text-secondary);">Error loading users</td></tr>';
    }
}

async function toggleAdminStatus(username, makeAdmin) {
    const action = makeAdmin ? 'make admin' : 'revoke admin';
    if (!confirm(`${makeAdmin ? 'Grant' : 'Revoke'} admin rights for ${username}?`)) return;
    
    try {
        const response = await fetch(`/api/filemanager/admin/set-admin?adminUsername=${currentUsername}&username=${encodeURIComponent(username)}&isAdmin=${makeAdmin}`, {
            method: 'POST'
        });
        
        if (response.ok) {
            alert(`Admin rights ${makeAdmin ? 'granted' : 'revoked'} successfully`);
            loadAdminUsers();
            
            if (username === currentUsername) {
                checkAdminStatus();
            }
        } else {
            const error = await response.text();
            throw new Error(error);
        }
    } catch (error) {
        alert(`Error changing admin status: ${error.message}`);
    }
}

async function togglePremiumStatus(username, makePremium) {
    const action = makePremium ? 'grant premium' : 'revoke premium';
    if (!confirm(`${makePremium ? 'Grant' : 'Revoke'} premium status for ${username}?`)) return;
    
    try {
        const response = await fetch(`/api/filemanager/admin/set-premium?adminUsername=${currentUsername}&username=${encodeURIComponent(username)}&isPremium=${makePremium}`, {
            method: 'POST'
        });
        
        if (response.ok) {
            alert(`Premium status ${makePremium ? 'granted' : 'revoked'} successfully`);
            loadAdminUsers();
            
            if (username === currentUsername) {
                loadUserInfo();
            }
        } else {
            const error = await response.text();
            throw new Error(error);
        }
    } catch (error) {
        alert(`Error changing premium status: ${error.message}`);
    }
}

function filterAdminUsers() {
    const search = document.getElementById('adminSearchInput').value.toLowerCase();
    const rows = document.querySelectorAll('#adminUsersList tr');
    
    rows.forEach(row => {
        const username = row.cells[0].textContent.toLowerCase();
        if (username.includes(search)) {
            row.style.display = '';
        } else {
            row.style.display = 'none';
        }
    });
}

function blockUser() {
    const username = document.getElementById('blockUsername').value.trim();
    const reason = document.getElementById('blockReason').value.trim();
    const days = document.getElementById('blockDays').value;
    
    if (!username || !reason) {
        alert('Please enter username and reason');
        return;
    }
    
    const url = `/api/filemanager/admin/block-user?adminUsername=${currentUsername}&usernameToBlock=${encodeURIComponent(username)}&reason=${encodeURIComponent(reason)}${days ? `&blockDays=${days}` : ''}`;
    
    fetch(url, { method: 'POST' })
        .then(response => {
            if (response.ok) {
                alert('User blocked successfully');
                loadAdminUsers();
                document.getElementById('blockUsername').value = '';
                document.getElementById('blockReason').value = '';
                document.getElementById('blockDays').value = '';
            } else {
                return response.text().then(text => { throw new Error(text) });
            }
        })
        .catch(error => {
            alert('Error blocking user: ' + error.message);
        });
}

function unblockUser(username) {
    if (!confirm(`Unblock user ${username}?`)) return;
    
    fetch(`/api/filemanager/admin/unblock-user?adminUsername=${currentUsername}&usernameToUnblock=${encodeURIComponent(username)}`, { method: 'POST' })
        .then(response => {
            if (response.ok) {
                alert('User unblocked successfully');
                loadAdminUsers();
            } else {
                return response.text().then(text => { throw new Error(text) });
            }
        })
        .catch(error => {
            alert('Error unblocking user: ' + error.message);
        });
}

function showBlockForm(username) {
    document.getElementById('blockUsername').value = username;
    document.getElementById('blockReason').focus();
}

let publicFiles = [];

function showPublicFilesPage() {
    mainContainer.classList.add('hidden');
    publicFilesContainer.classList.remove('hidden');
    fileDetailsContainer.classList.add('hidden');
    loadPublicFiles();

    const searchInput = document.getElementById('publicSearchInput');
    let searchTimeout;
    searchInput.addEventListener('input', () => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(filterPublicFiles, 300);
    });
}

async function loadPublicFiles() {
    const publicFilesList = document.getElementById('publicFilesList');
    publicFilesList.innerHTML = '<div class="text-muted"><i class="fas fa-spinner fa-spin"></i> Loading...</div>';

    try {
        const response = await fetch('/api/filemanager/public-files');
        if (response.ok) {
            publicFiles = await response.json();
            renderPublicFiles(publicFiles);
        } else {
            publicFilesList.innerHTML = '<div class="text-muted">Error loading public files.</div>';
        }
    } catch (error) {
        publicFilesList.innerHTML = '<div class="text-muted">Error loading public files.</div>';
    }
}

function renderPublicFiles(files) {
    const publicFilesList = document.getElementById('publicFilesList');
    publicFilesList.innerHTML = '';

    if (files.length === 0) {
        publicFilesList.innerHTML = '<div class="text-muted">No public files available.</div>';
        return;
    }

    files.forEach(file => {
        const fileCard = document.createElement('div');
        fileCard.className = 'public-file-card card';
        const isLiked = file.likedBy && file.likedBy.includes(currentUsername);
        
        fileCard.innerHTML = `
            <div class="public-file-header">
                <i class="fas ${getFileIcon(file.filePath)} file-icon"></i>
                <div class="file-name">${file.filePath.split('/').pop()}</div>
            </div>
            <div class="public-file-meta">
                <span>${new Date(file.uploadedDate).toLocaleDateString()}</span>
            </div>
            <div class="public-file-meta">
                <span>Likes: ${file.likes || 0}</span>
                <span>Downloads: ${file.downloadCount || 0}</span>
                <span>Comments: ${file.comments ? file.comments.length : 0}</span>
            </div>
            <div class="public-file-actions">
                <button class="btn" onclick="showFileDetails('${file.id}')">
                    <i class="fas fa-info-circle"></i> View Details
                </button>
            </div>
        `;
        publicFilesList.appendChild(fileCard);
    });
}

async function toggleLike(fileId) {
    try {
        const response = await fetch(`/api/filemanager/toggle-like?username=${encodeURIComponent(currentUsername)}&fileId=${encodeURIComponent(fileId)}`, {
            method: 'POST'
        });
        if (response.ok) {
            if (document.getElementById('fileDetailsContainer').classList.contains('hidden')) {
                loadPublicFiles();
            } else {
                showFileDetails(fileId);
            }
        } else {
            const error = await response.text();
            alert(`Failed to toggle like: ${error}`);
        }
    } catch (error) {
        alert('Error toggling like.');
    }
}

async function addComment(fileId, inputId = null) {
    const commentInput = inputId ? 
        document.getElementById(inputId) : 
        document.getElementById(`commentInput_${fileId}`);
    const commentText = commentInput.value.trim();
    
    if (!commentText) {
        alert('Please enter a comment.');
        return;
    }

    try {
        const response = await fetch(
            `/api/filemanager/add-comment?username=${encodeURIComponent(currentUsername)}&fileId=${encodeURIComponent(fileId)}`,
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(commentText)
            }
        );
        
        if (response.ok) {
            commentInput.value = '';
            if (document.getElementById('fileDetailsContainer').classList.contains('hidden')) {
                loadPublicFiles();
            } else {
                showFileDetails(fileId);
            }
        } else {
            const error = await response.json();
            alert(`Failed to add comment: ${error.title || error}`);
        }
    } catch (error) {
        alert('Error adding comment: ' + error.message);
    }
}

function filterPublicFiles() {
    const query = document.getElementById('publicSearchInput').value.trim().toLowerCase();
    if (!query) {
        renderPublicFiles(publicFiles);
        return;
    }

    const filteredFiles = publicFiles.filter(file => {
        const fileName = file.filePath.toLowerCase();
        const username = file.username.toLowerCase();
        return fileName.includes(query) || username.includes(query);
    });
    renderPublicFiles(filteredFiles);
}

function sortPublicFiles(key, direction) {
    const sortedFiles = [...publicFiles];
    sortedFiles.sort((a, b) => {
        let valA, valB;
        switch (key) {
            case 'name':
                valA = a.filePath.toLowerCase();
                valB = b.filePath.toLowerCase();
                break;
            case 'date':
                valA = new Date(a.uploadedDate).getTime();
                valB = new Date(b.uploadedDate).getTime();
                break;
            case 'likes':
                valA = a.likes || 0;
                valB = b.likes || 0;
                break;
            case 'downloads':
                valA = a.downloadCount || 0;
                valB = b.downloadCount || 0;
                break;
        }
        if (direction === 'asc') {
            return valA < valB ? -1 : valA > valB ? 1 : 0;
        } else {
            return valA > valB ? -1 : valA < valB ? 1 : 0;
        }
    });
    renderPublicFiles(sortedFiles);
}

async function showProfileModal(username) {
    viewedUsername = username;
    const modal = document.getElementById('profileModal');
    const profileUsername = document.getElementById('profileUsername');
    const subscribersCount = document.getElementById('profileSubscribersCount');
    const subscriptionsCount = document.getElementById('profileSubscriptionsCount');
    const isPremium = document.getElementById('profileIsPremium');
    const subscribeBtn = document.getElementById('subscribeBtn');
    const unsubscribeBtn = document.getElementById('unsubscribeBtn');
    const publicFilesList = document.getElementById('publicFilesList');

    profileUsername.textContent = username;
    publicFilesList.innerHTML = '<div class="text-muted"><i class="fas fa-spinner fa-spin"></i> Loading...</div>';

    try {
        const response = await fetch(`/api/filemanager/profile?username=${encodeURIComponent(username)}&viewerUsername=${encodeURIComponent(currentUsername)}`);
        if (response.ok) {
            const profileData = await response.json();

            subscribersCount.textContent = profileData.subscribersCount;
            subscriptionsCount.textContent = profileData.subscriptionsCount;
            isPremium.textContent = profileData.isPremium ? 'Yes' : 'No';

            if (username !== currentUsername) {
                document.getElementById('subscriptionActions').classList.remove('hidden');
                if (profileData.isSubscribed) {
                    subscribeBtn.style.display = 'none';
                    unsubscribeBtn.style.display = 'inline-flex';
                } else {
                    subscribeBtn.style.display = 'inline-flex';
                    unsubscribeBtn.style.display = 'none';
                }
            } else {
                document.getElementById('subscriptionActions').classList.add('hidden');
            }

            publicFilesList.innerHTML = '';
            if (profileData.publicFiles && profileData.publicFiles.length > 0) {
                profileData.publicFiles.forEach(file => {
                    const fileCard = document.createElement('div');
                    fileCard.className = 'file-card';
                    fileCard.innerHTML = `
                        <div class="file-header">
                            <i class="fas ${getFileIcon(file.filePath)} file-icon"></i>
                            <div class="file-name">${file.filePath.split('/').pop()}</div>
                        </div>
                        <div class="space-meta">
                            <span>Space: ${file.spaceName}</span>
                            <span>${new Date(file.uploadedDate).toLocaleDateString()}</span>
                        </div>
                        <div class="file-actions">
                            <button class="action-btn" onclick="downloadPublicFile('${file.id}')">
                                <i class="fas fa-download"></i> Download
                            </button>
                            <button class="action-btn" onclick="previewPublicFile('${username}', '${file.spaceName}', '${file.filePath}')">
                                <i class="fas fa-eye"></i> Preview
                            </button>
                        </div>
                    `;
                    publicFilesList.appendChild(fileCard);
                });
            } else {
                publicFilesList.innerHTML = '<div class="text-muted">No public files available.</div>';
            }
        } else {
            const error = await response.text();
            publicFilesList.innerHTML = `<div class="text-muted">Error loading profile: ${error}</div>`;
        }
    } catch (error) {
        publicFilesList.innerHTML = '<div class="text-muted">Error loading profile.</div>';
    }

    showModal('profileModal');
}

async function subscribeToUser() {
    try {
        const response = await fetch(`/api/filemanager/subscribe?username=${encodeURIComponent(currentUsername)}&targetUsername=${encodeURIComponent(viewedUsername)}`, {
            method: 'POST'
        });
        if (response.ok) {
            alert(`Subscribed to ${viewedUsername}`);
            showProfileModal(viewedUsername);
        } else {
            const error = await response.text();
            alert(`Failed to subscribe: ${error}`);
        }
    } catch (error) {
        alert('Error subscribing to user.');
    }
}

async function unsubscribeFromUser() {
    try {
        const response = await fetch(`/api/filemanager/unsubscribe?username=${encodeURIComponent(currentUsername)}&targetUsername=${encodeURIComponent(viewedUsername)}`, {
            method: 'POST'
        });
        if (response.ok) {
            alert(`Unsubscribed from ${viewedUsername}`);
            showProfileModal(viewedUsername);
        } else {
            const error = await response.text();
            alert(`Failed to unsubscribe: ${error}`);
        }
    } catch (error) {
        alert('Error unsubscribing from user.');
    }
}

let selectedFilesToPublish = new Set();

function showPublishFilesModal() {
    const modal = document.getElementById('publishFilesModal');
    const spaceSelect = document.getElementById('publishSpaceSelect');
    const fileList = document.getElementById('publishFileList');
    const description = document.getElementById('publishDescription');

    spaceSelect.innerHTML = '<option value="">Select a space</option>';
    fileList.innerHTML = '';
    description.value = '';
    selectedFilesToPublish.clear();

    fetch(`/api/filemanager/userinfo?username=${currentUsername}`)
        .then(response => response.json())
        .then(data => {
            data.spaces.forEach(space => {
                const option = document.createElement('option');
                option.value = space.name;
                option.textContent = space.name;
                if (space.name === currentSpace) option.selected = true;
                spaceSelect.appendChild(option);
            });
            if (currentSpace) {
                loadFilesForPublishing();
            }
            showModal('publishFilesModal');
        })
        .catch(error => {
            console.error('Error loading spaces:', error);
            alert('Failed to load spaces.');
        });
}

async function loadFilesForPublishing() {
    const spaceSelect = document.getElementById('publishSpaceSelect');
    const fileList = document.getElementById('publishFileList');
    const selectedSpace = spaceSelect.value;

    if (!selectedSpace) {
        fileList.innerHTML = '<div class="text-muted">Please select a space first.</div>';
        return;
    }

    fileList.innerHTML = '<div class="text-muted"><i class="fas fa-spinner fa-spin"></i> Loading files...</div>';

    try {
        const response = await fetch(`/api/filemanager/files?username=${currentUsername}&spaceName=${selectedSpace}`);
        if (response.ok) {
            const files = await response.json();
            fileList.innerHTML = '';

            if (files.length === 0) {
                fileList.innerHTML = '<div class="text-muted">No files available in this space.</div>';
                return;
            }

            files.forEach(file => {
                if (file.type === 'file') {
                    const fileItem = document.createElement('div');
                    fileItem.className = 'file-item';
                    fileItem.innerHTML = `
                        <input type="checkbox" id="publish_${file.path}" value="${file.path}">
                        <div class="file-info">
                            <span class="file-name">${file.path}</span>
                            <span class="file-size">${formatFileSize(file.size)}</span>
                        </div>
                    `;
                    fileList.appendChild(fileItem);

                    const checkbox = fileItem.querySelector('input');
                    checkbox.addEventListener('change', () => {
                        if (checkbox.checked) {
                            selectedFilesToPublish.add(file.path);
                        } else {
                            selectedFilesToPublish.delete(file.path);
                        }
                    });
                }
            });
        } else {
            fileList.innerHTML = '<div class="text-muted">Error loading files.</div>';
        }
    } catch (error) {
        fileList.innerHTML = '<div class="text-muted">Error loading files.</div>';
        console.error('Error loading files:', error);
    }
}

async function publishSelectedFiles() {
    const spaceSelect = document.getElementById('publishSpaceSelect');
    const description = document.getElementById('publishDescription').value.trim();
    const selectedSpace = spaceSelect.value;

    if (!selectedSpace) {
        alert('Please select a space.');
        return;
    }

    if (selectedFilesToPublish.size === 0) {
        alert('Please select at least one file to publish.');
        return;
    }

    try {
        const publishPromises = Array.from(selectedFilesToPublish).map(async filename => {
            const response = await fetch(
                `/api/filemanager/publish-file?username=${encodeURIComponent(currentUsername)}&spaceName=${encodeURIComponent(selectedSpace)}&filename=${encodeURIComponent(filename)}`,
                {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify(description || 'No description provided')
                }
            );
            if (!response.ok) {
                const error = await response.text();
                throw new Error(`Failed to publish ${filename}: ${error}`);
            }
            return filename;
        });

        await Promise.all(publishPromises);
        alert(`Successfully published ${selectedFilesToPublish.size} file(s).`);
        closeModal('publishFilesModal');
        loadPublicFiles();
    } catch (error) {
        console.error('Error publishing files:', error);
        alert(`Error publishing files: ${error.message}`);
    }
}

function downloadPublicFile(fileId) {
    window.open(`/api/filemanager/marketplace/download?fileid=${encodeURIComponent(fileId)}`, '_blank');
}

function previewPublicFile(username, spaceName, filePath) {
    previewFile(filePath);
}

async function showFileDetails(fileId) {
    currentFileId = fileId;
    mainContainer.classList.add('hidden');
    publicFilesContainer.classList.add('hidden');
    fileDetailsContainer.classList.remove('hidden');

    try {
        const response = await fetch(`/api/filemanager/public-file-details?fileId=${encodeURIComponent(fileId)}&username=${encodeURIComponent(currentUsername)}`);
        if (response.ok) {
            const fileDetails = await response.json();
            viewedFileOwner = fileDetails.username;
            
            document.getElementById('fileDetailsTitle').textContent = fileDetails.filePath.split('/').pop();
            document.getElementById('fileDetailsAuthor').innerHTML = `Uploaded by: <a href="javascript:void(0)" class="user-link" onclick="showProfileModal('${fileDetails.username}')">${fileDetails.username}</a>`;
            document.getElementById('fileDetailsDescription').textContent = fileDetails.description || 'No description provided';
            document.getElementById('fileDetailsDate').textContent = new Date(fileDetails.uploadedDate).toLocaleString();
            document.getElementById('fileDetailsDownloads').textContent = fileDetails.downloadCount || 0;
            document.getElementById('fileDetailsLikes').textContent = fileDetails.likes || 0;
            document.getElementById('fileDetailsCommentsCount').textContent = fileDetails.comments ? fileDetails.comments.length : 0;
            
            const likeBtn = document.getElementById('fileDetailsLikeBtn');
            likeBtn.innerHTML = `<i class="fas fa-heart${fileDetails.isLiked ? '' : '-o'}"></i> ${fileDetails.isLiked ? 'Unlike' : 'Like'}`;
            likeBtn.classList.toggle('liked', fileDetails.isLiked);
            
            const fileActions = document.getElementById('fileDetailsActions');
            fileActions.innerHTML = `
                <button class="btn" id="fileDetailsLikeBtn" onclick="toggleLike(currentFileId)">
                    <i class="fas fa-heart"></i> Like
                </button>
                <button class="btn btn-secondary" onclick="downloadPublicFile(currentFileId)">
                    <i class="fas fa-download"></i> Download
                </button>
            `;

            if (fileDetails.username === currentUsername) {
                fileActions.innerHTML += `
                    <button class="btn btn-secondary" onclick="deletePublicFile('${fileDetails.id}')">
                        <i class="fas fa-trash"></i> Delete File
                    </button>
                `;
            }
            
            loadFilePreview(fileDetails);
            
            renderComments(fileDetails.comments || []);
        } else {
            const error = await response.text();
            alert(`Error loading file details: ${error}`);
            showPublicFilesPage();
        }
    } catch (error) {
        console.error('Error loading file details:', error);
        alert('Error loading file details');
        showPublicFilesPage();
    }
}

async function loadFilePreview(fileDetails) {
    const previewContainer = document.getElementById('fileDetailsPreview');
    previewContainer.innerHTML = '<div class="text-muted"><i class="fas fa-spinner fa-spin"></i> Loading preview...</div>';
    
    const extension = fileDetails.filePath.substring(fileDetails.filePath.lastIndexOf('.')).toLowerCase();
    
    if (['.jpg', '.jpeg', '.png', '.gif'].includes(extension)) {
        previewContainer.innerHTML = `
            <img src="/api/filemanager/public-preview?fileId=${encodeURIComponent(fileDetails.id)}" 
                style="max-width: 100%; max-height: 500px; border-radius: 6px;">
        `;
    } else if (['.mp4', '.webm', '.mov'].includes(extension)) {
        previewContainer.innerHTML = `
            <video controls style="max-width: 100%; max-height: 500px;">
                <source src="/api/filemanager/public-preview?fileId=${encodeURIComponent(fileDetails.id)}" 
                        type="video/${extension.substring(1)}">
                Your browser does not support the video tag.
            </video>
        `;
    } else if (['.mp3', '.wav', '.flac'].includes(extension)) {
        previewContainer.innerHTML = `
            <audio controls style="width: 100%;">
                <source src="/api/filemanager/public-preview?fileId=${encodeURIComponent(fileDetails.id)}" 
                        type="audio/${extension.substring(1)}">
                Your browser does not support the audio element.
            </audio>
        `;
    } else if (['.txt', '.json', '.xml', '.csv', '.pdf'].includes(extension)) {
        previewContainer.innerHTML = `
            <iframe src="/api/filemanager/public-preview?fileId=${encodeURIComponent(fileDetails.id)}" 
                    style="width: 100%; height: 500px; border: none;"></iframe>
        `;
    } else {
        previewContainer.innerHTML = `
            <div class="text-muted">
                <i class="fas ${getFileIcon(fileDetails.filePath)} fa-5x"></i>
                <p>Preview not available for this file type</p>
            </div>
        `;
    }
}
function renderComments(comments) {
    const commentsContainer = document.getElementById('fileDetailsComments');
    commentsContainer.innerHTML = '';
    
    if (comments.length === 0) {
        commentsContainer.innerHTML = '<div class="text-muted">No comments yet</div>';
        return;
    }
    
    comments.forEach(comment => {
        const commentElement = document.createElement('div');
        commentElement.className = 'comment';
        commentElement.innerHTML = `
            <div style="display: flex; justify-content: space-between; align-items: center;">
                <div>
                    <strong>${comment.author}:</strong> ${comment.text}
                    <div class="text-muted" style="font-size: 12px; margin-top: 5px;">
                        ${new Date(comment.date).toLocaleString()}
                    </div>
                </div>
                ${(comment.username === currentUsername || viewedFileOwner === currentUsername) ? `
                <button class="action-btn" onclick="deleteComment('${comment.id}')" style="margin-left: 10px;">
                    <i class="fas fa-trash"></i> Delete
                </button>
                ` : ''}
            </div>
        `;
        commentsContainer.appendChild(commentElement);
    });
}
let viewedFileOwner = null;

async function deleteComment(commentId) {
    if (!confirm('Are you sure you want to delete this comment?')) return;
    
    try {
        const response = await fetch(
            `/api/filemanager/delete-comment?username=${encodeURIComponent(currentUsername)}&fileId=${encodeURIComponent(currentFileId)}&commentId=${encodeURIComponent(commentId)}`,
            { method: 'POST' }
        );
        
        if (response.ok) {
            alert('Comment deleted successfully');
            showFileDetails(currentFileId);
        } else {
            const error = await response.text();
            alert(`Failed to delete comment: ${error}`);
        }
    } catch (error) {
        console.error('Error deleting comment:', error);
        alert('Error deleting comment');
    }
}

async function deletePublicFile(fileId) {
    if (!confirm('Are you sure you want to delete this public file? This will remove it from the marketplace but keep the original file in your space.')) return;
    
    try {
        const response = await fetch(
            `/api/filemanager/delete-public-file?username=${encodeURIComponent(currentUsername)}&fileId=${encodeURIComponent(fileId)}`,
            { method: 'POST' }
        );
        
        if (response.ok) {
            alert('Public file deleted successfully');
            showPublicFilesPage();
        } else {
            const error = await response.text();
            alert(`Failed to delete public file: ${error}`);
        }
    } catch (error) {
        console.error('Error deleting public file:', error);
        alert('Error deleting public file');
    }
}

function showSettingsModal() {
    if (currentUsername) {
        fetch(`/api/filemanager/userinfo?username=${currentUsername}`)
            .then(response => response.json())
            .then(data => {
                const limit = data.isPremium ? '6 GB' : '2 GB';
                const usedStorage = data.usedStorage || 0;
                const usedStorageGB = (usedStorage / 1_000_000_000).toFixed(2);
                const usedPercent = ((usedStorage / (data.isPremium ? 6 : 2) / 1_000_000_000) * 100).toFixed(1);
                
                document.getElementById('settingsAccountInfo').innerHTML = `
                    <div class="account-details">
                        <p><strong>Username:</strong> ${currentUsername}</p>
                        <p><strong>Status:</strong> ${data.isPremium ? '<span class="premium-badge"><i class="fas fa-crown"></i> Premium</span>' : 'Basic'}</p>
                        <p><strong>Storage:</strong> ${usedStorageGB} GB / ${limit} (${usedPercent}%)</p>
                        <div class="storage-progress">
                            <progress value="${usedPercent}" max="100" style="width: 100%;"></progress>
                        </div>
                        <p><strong>Spaces:</strong> ${data.spaces ? data.spaces.length : 0}</p>
                        ${data.isAdmin ? '<p><span class="admin-badge"><i class="fas fa-user-shield"></i> Admin</span></p>' : ''}
                    </div>
                `;
            });
    }
    
    const themeToggleSettings = document.getElementById('themeToggleSettings');
    if (document.body.classList.contains('light-theme')) {
        themeToggleSettings.innerHTML = '<i class="fas fa-sun"></i> Light Mode';
    } else {
        themeToggleSettings.innerHTML = '<i class="fas fa-moon"></i> Dark Mode';
    }
    
    showModal('settingsModal');
}