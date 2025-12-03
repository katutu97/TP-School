// Используем объект scheduleServerData, определенный в секции Scripts Razor View

// Модальное окно для домашнего задания
let currentHomeworkData = null;

function openHomeworkModal(lessonId, subject, date, lessonNumber, teacher, lessonTopic, homeworkText) {
    // Сохраняем данные
    currentHomeworkData = {
        lessonId: lessonId,
        subject: subject,
        date: date,
        lessonNumber: lessonNumber,
        teacher: teacher,
        lessonTopic: lessonTopic,
        homeworkText: homeworkText
    };

    // Заполняем модальное окно
    document.getElementById('modalSubject').textContent = subject;
    document.getElementById('modalDate').textContent = date;
    document.getElementById('modalLessonNumber').textContent = 'Урок ' + lessonNumber;
    document.getElementById('modalTeacher').textContent = teacher;
    document.getElementById('modalLessonTopic').textContent = lessonTopic;
    document.getElementById('modalHomeworkText').textContent = homeworkText;
    document.getElementById('modalLessonId').value = lessonId;

    // Загружаем файлы (если есть) - закомментировано в оригинале
    // loadHomeworkFiles(lessonId);

    // Показываем модальное окно
    document.getElementById('homeworkModal').classList.remove('hidden');
    document.body.style.overflow = 'hidden';
}

function closeHomeworkModal() {
    document.getElementById('homeworkModal').classList.add('hidden');
    document.body.style.overflow = 'auto';

    // Очищаем форму
    document.getElementById('studentAnswer').value = '';
    document.getElementById('homeworkFiles').value = '';
    document.getElementById('fileList').innerHTML = '';
    currentHomeworkData = null;
}

// Загрузка файлов домашнего задания (для примера, если бы fetch был активен)
// async function loadHomeworkFiles(lessonId) { ... }

// Обработка выбора файлов
document.getElementById('homeworkFiles').addEventListener('change', function (e) {
    const fileList = document.getElementById('fileList');
    fileList.innerHTML = '';

    Array.from(e.target.files).forEach((file, index) => {
        const fileItem = document.createElement('div');
        fileItem.className = 'flex items-center justify-between p-2 bg-gray-50 rounded border';
        fileItem.innerHTML = `
            <div class="flex items-center">
                <i class="fas fa-file ${getFileIcon(file.name)} text-gray-400 mr-2"></i>
                <div>
                    <p class="text-sm text-gray-700 truncate max-w-[200px]">${file.name}</p>
                    <p class="text-xs text-gray-500">${formatFileSize(file.size)}</p>
                </div>
            </div>
            <button type="button" onclick="removeFile(${index})" class="text-red-500 hover:text-red-700">
                <i class="fas fa-times"></i>
            </button>
        `;
        fileList.appendChild(fileItem);
    });
});

function getFileIcon(filename) {
    const ext = filename.split('.').pop().toLowerCase();
    if (['pdf'].includes(ext)) return 'fa-file-pdf text-red-500';
    if (['doc', 'docx'].includes(ext)) return 'fa-file-word text-blue-500';
    if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) return 'fa-file-image text-green-500';
    if (['txt'].includes(ext)) return 'fa-file-alt text-gray-500';
    return 'fa-file text-gray-400';
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function removeFile(index) {
    const dt = new DataTransfer();
    const input = document.getElementById('homeworkFiles');
    const { files } = input;

    for (let i = 0; i < files.length; i++) {
        const file = files[i];
        if (index !== i) {
            dt.items.add(file);
        }
    }

    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
}

// Обработка отправки формы
document.getElementById('submitHomeworkForm').addEventListener('submit', async function (e) {
    e.preventDefault();

    const formData = new FormData(this);
    const studentAnswer = document.getElementById('studentAnswer').value;
    const files = document.getElementById('homeworkFiles').files;

    if (!studentAnswer.trim() && files.length === 0) {
        showNotification('Пожалуйста, напишите ответ или прикрепите файл', 'warning');
        return;
    }

    // Показываем индикатор загрузки
    const submitBtn = this.querySelector('button[type="submit"]');
    const originalText = submitBtn.innerHTML;
    const originalDisabled = submitBtn.disabled;

    submitBtn.innerHTML = '<i class="fas fa-spinner fa-spin mr-2"></i>Отправка...';
    submitBtn.disabled = true;

    try {
        // Здесь AJAX запрос на сервер, используем переданный URL
        const response = await fetch(scheduleServerData.submitHomeworkUrl, {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (result.success) {
            showNotification('Задание успешно отправлено!', 'success');

            // Закрываем модальное окно через 1.5 секунды
            setTimeout(() => {
                closeHomeworkModal();
            }, 1500);
        } else {
            showNotification(result.message || 'Ошибка при отправке задания', 'error');
        }

    } catch (error) {
        console.error('Ошибка отправки:', error);
        showNotification('Ошибка сети при отправке задания', 'error');
    } finally {
        submitBtn.innerHTML = originalText;
        submitBtn.disabled = originalDisabled;
    }
});

// Утилита для показа уведомлений
function showNotification(message, type = 'info') {
    const container = document.getElementById('notificationContainer');
    const notification = document.createElement('div');

    const bgColor = type === 'success' ? 'bg-green-500' :
        type === 'error' ? 'bg-red-500' :
            type === 'warning' ? 'bg-yellow-500' : 'bg-blue-500';

    const icon = type === 'success' ? 'fa-check-circle' :
        type === 'error' ? 'fa-exclamation-circle' :
            type === 'warning' ? 'fa-exclamation-triangle' : 'fa-info-circle';

    notification.className = `${bgColor} text-white p-4 rounded-lg shadow-lg max-w-md transform transition-all duration-300 translate-x-full`;
    notification.innerHTML = `
        <div class="flex items-center">
            <i class="fas ${icon} mr-3 text-xl"></i>
            <div class="flex-1">
                <p class="font-medium">${message}</p>
            </div>
            <button type="button" onclick="this.parentElement.parentElement.remove()" class="ml-3 text-white hover:text-gray-200">
                <i class="fas fa-times"></i>
            </button>
        </div>
    `;

    container.appendChild(notification);

    // Анимация появления
    setTimeout(() => {
        notification.classList.remove('translate-x-full');
        notification.classList.add('translate-x-0');
    }, 10);

    // Автоматически скрыть через 4 секунды
    setTimeout(() => {
        notification.classList.remove('translate-x-0');
        notification.classList.add('translate-x-full');
        setTimeout(() => {
            if (notification.parentElement) {
                notification.remove();
            }
        }, 300);
    }, 4000);
}

// Закрытие по ESC
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !document.getElementById('homeworkModal').classList.contains('hidden')) {
        closeHomeworkModal();
    }
});

// Закрытие по клику вне модального окна
document.getElementById('homeworkModal').addEventListener('click', function (e) {
    if (e.target === this) {
        closeHomeworkModal();
    }
});

// Переключение между детьми (для родителей)
function switchChild() {
    const childSelector = document.getElementById('childSelector');
    const selectedChildId = childSelector.value;
    const selectedChildName = childSelector.options[childSelector.selectedIndex].getAttribute('data-name');

    // Получаем текущие параметры URL
    const urlParams = new URLSearchParams(window.location.search);
    // Используем todayDate из серверных данных как запасной вариант
    const selectedDate = urlParams.get('selectedDate') || scheduleServerData.todayDate;

    // Формируем новый URL с параметром childId
    const newUrl = scheduleServerData.scheduleIndexUrl +
        '?selectedDate=' + encodeURIComponent(selectedDate) +
        '&childId=' + selectedChildId;

    // Показываем загрузку
    showLoading('Загружаем расписание для ' + selectedChildName + '...');

    window.location.href = newUrl;
}

function showLoading(message) {
    // Создаем overlay загрузки
    const overlay = document.createElement('div');
    overlay.id = 'loadingOverlay';
    overlay.className = 'fixed inset-0 bg-white bg-opacity-90 z-50 flex flex-col items-center justify-center';
    overlay.innerHTML = `
        <div class="animate-spin rounded-full h-16 w-16 border-t-2 border-b-2 border-blue-500 mb-4"></div>
        <p class="text-gray-700 text-lg font-medium">${message}</p>
        <p class="text-gray-500 text-sm mt-2">Пожалуйста, подождите...</p>
    `;
    document.body.appendChild(overlay);
}

// Удаляем overlay при загрузке страницы, инициализируем Drag and Drop
document.addEventListener('DOMContentLoaded', function () {
    const overlay = document.getElementById('loadingOverlay');
    if (overlay) overlay.remove();

    // Drag and drop для файлов
    const dropArea = document.querySelector('label[for="homeworkFiles"]');

    if (dropArea) {
        // Предотвращаем стандартное поведение браузера
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            dropArea.addEventListener(eventName, preventDefaults, false);
        });

        function preventDefaults(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        // Подсветка области при наведении
        ['dragenter', 'dragover'].forEach(eventName => {
            dropArea.addEventListener(eventName, highlight, false);
        });

        ['dragleave', 'drop'].forEach(eventName => {
            dropArea.addEventListener(eventName, unhighlight, false);
        });

        function highlight() {
            dropArea.classList.add('border-blue-500', 'bg-blue-50');
        }

        function unhighlight() {
            dropArea.classList.remove('border-blue-500', 'bg-blue-50');
        }

        // Обработка сброса файлов
        dropArea.addEventListener('drop', handleDrop, false);

        function handleDrop(e) {
            const dt = e.dataTransfer;
            const files = dt.files;

            const input = document.getElementById('homeworkFiles');
            input.files = files;
            // Принудительно вызываем событие 'change' для обновления списка файлов
            input.dispatchEvent(new Event('change'));
        }
    }
});