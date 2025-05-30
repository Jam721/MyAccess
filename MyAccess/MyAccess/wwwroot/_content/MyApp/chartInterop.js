// wwwroot/_content/MyApp/chartInterop.js

// Глобальное хранилище экземпляров графиков
const chartInstances = {};

export function createChart(canvasId, chartType, labels, data) {
    // Удаляем старый график, если он существует
    if (chartInstances[canvasId]) {
        chartInstances[canvasId].destroy();
    }

    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error(`Canvas with id ${canvasId} not found`);
        return;
    }

    const ctx = canvas.getContext('2d');

    // Генерация цветов
    const colors = generateColors(labels.length);

    // Конфигурация графика
    const config = {
        type: chartType,
        data: {
            labels: labels,
            datasets: [{
                label: 'Значения',
                data: data,
                backgroundColor: colors,
                borderColor: ['bar', 'line'].includes(chartType)
                    ? colors.map(c => c.replace('60%', '40%'))
                    : colors,
                borderWidth: 2
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'top',
                },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            const value = context.parsed.y || context.parsed;
                            const total = context.chart.data.datasets[0].data.reduce((a, b) => a + b, 0);
                            const percentage = total > 0 ? (value / total * 100).toFixed(1) : 0;
                            return `${value.toFixed(2)} (${percentage}%)`;
                        }
                    }
                }
            }
        }
    };

    // Создаем и сохраняем новый график
    chartInstances[canvasId] = new Chart(ctx, config);
    return chartInstances[canvasId];
}

export function exportChart(canvasId, filename) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error(`Canvas with id ${canvasId} not found`);
        return;
    }

    const link = document.createElement('a');
    link.download = `${filename}.png`;
    link.href = canvas.toDataURL('image/png');
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}

function generateColors(count) {
    const colors = [];
    const hueStep = 360 / count;

    for (let i = 0; i < count; i++) {
        const hue = i * hueStep;
        colors.push(`hsl(${hue}, 70%, 60%)`);
    }

    return colors;
}