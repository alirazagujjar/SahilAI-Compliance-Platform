window.SahilCharts = (function () {
    let donutChart = null;
    let barChart = null;

    return {
        initDonut(canvasId, labels, data, colors) {
            const el = document.getElementById(canvasId);
            if (!el) return;
            if (donutChart) { donutChart.destroy(); donutChart = null; }
            donutChart = new Chart(el.getContext('2d'), {
                type: 'doughnut',
                data: {
                    labels,
                    datasets: [{ data, backgroundColor: colors, borderWidth: 0, hoverOffset: 6 }]
                },
                options: {
                    cutout: '74%',
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: { label: ctx => ` ${ctx.label}: ${ctx.parsed}` }
                        }
                    },
                    animation: { animateRotate: true, duration: 700 }
                }
            });
        },

        updateDonut(data) {
            if (!donutChart) return;
            donutChart.data.datasets[0].data = data;
            donutChart.update('none');
        },

        initBar(canvasId, labels, data) {
            const el = document.getElementById(canvasId);
            if (!el) return;
            if (barChart) { barChart.destroy(); barChart = null; }
            barChart = new Chart(el.getContext('2d'), {
                type: 'bar',
                data: {
                    labels,
                    datasets: [{
                        label: 'Invoices',
                        data,
                        backgroundColor: 'rgba(99,102,241,0.75)',
                        hoverBackgroundColor: 'rgba(99,102,241,1)',
                        borderRadius: 6,
                        borderSkipped: false
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: { callbacks: { label: ctx => ` ${ctx.parsed.y} invoice${ctx.parsed.y !== 1 ? 's' : ''}` } }
                    },
                    scales: {
                        y: {
                            beginAtZero: true,
                            ticks: { stepSize: 1, color: '#94a3b8', font: { size: 11 } },
                            grid: { color: 'rgba(148,163,184,0.12)' },
                            border: { display: false }
                        },
                        x: {
                            ticks: { color: '#94a3b8', font: { size: 11 } },
                            grid: { display: false },
                            border: { display: false }
                        }
                    }
                }
            });
        },

        updateBar(data) {
            if (!barChart) return;
            barChart.data.datasets[0].data = data;
            barChart.update('none');
        },

        destroyAll() {
            if (donutChart) { donutChart.destroy(); donutChart = null; }
            if (barChart)   { barChart.destroy();   barChart   = null; }
        }
    };
})();
