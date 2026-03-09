// Canvas drag and drop functionality
window.canvasInterop = {
    dotNetRef: null,
    isDragging: false,
    dragTarget: null,
    dragType: null,
    dragId: 0,
    startX: 0,
    startY: 0,
    offsetX: 0,
    offsetY: 0,

    // Connection dragging
    isConnecting: false,
    connectFromAgentId: 0,
    tempLine: null,

    init: function(dotNetReference) {
        this.dotNetRef = dotNetReference;

        const container = document.querySelector('.canvas-container');
        if (!container) return;

        // Mouse move for dragging
        container.addEventListener('mousemove', (e) => {
            if (this.isDragging && this.dragTarget) {
                const rect = container.getBoundingClientRect();
                const x = e.clientX - rect.left - this.offsetX;
                const y = e.clientY - rect.top - this.offsetY;

                this.dragTarget.style.left = Math.max(0, x) + 'px';
                this.dragTarget.style.top = Math.max(0, y) + 'px';
            }

            if (this.isConnecting && this.tempLine) {
                const rect = container.getBoundingClientRect();
                this.tempLine.setAttribute('x2', e.clientX - rect.left);
                this.tempLine.setAttribute('y2', e.clientY - rect.top);
            }
        });

        // Mouse up to end drag
        container.addEventListener('mouseup', async (e) => {
            if (this.isDragging && this.dragTarget) {
                const rect = container.getBoundingClientRect();
                const x = parseFloat(this.dragTarget.style.left) || 0;
                const y = parseFloat(this.dragTarget.style.top) || 0;

                await this.dotNetRef.invokeMethodAsync('UpdatePosition', this.dragType, this.dragId, x, y);

                this.isDragging = false;
                this.dragTarget.style.cursor = 'grab';
                this.dragTarget = null;
            }

            if (this.isConnecting) {
                // Check if dropped on a team node
                const teamNode = e.target.closest('.team-node');
                if (teamNode && this.connectFromAgentId > 0) {
                    const teamId = parseInt(teamNode.dataset.teamId);
                    if (teamId > 0) {
                        await this.dotNetRef.invokeMethodAsync('ConnectAgentToTeam', this.connectFromAgentId, teamId);
                    }
                }

                if (this.tempLine) {
                    this.tempLine.remove();
                    this.tempLine = null;
                }
                this.isConnecting = false;
                this.connectFromAgentId = 0;
            }
        });

        // Handle leaving container
        container.addEventListener('mouseleave', () => {
            if (this.isDragging) {
                this.isDragging = false;
                if (this.dragTarget) {
                    this.dragTarget.style.cursor = 'grab';
                }
                this.dragTarget = null;
            }
            if (this.isConnecting && this.tempLine) {
                this.tempLine.remove();
                this.tempLine = null;
                this.isConnecting = false;
            }
        });
    },

    startDrag: function(element, type, id) {
        this.isDragging = true;
        this.dragTarget = element;
        this.dragType = type;
        this.dragId = id;

        const rect = element.getBoundingClientRect();
        const containerRect = element.closest('.canvas-container').getBoundingClientRect();

        this.offsetX = event.clientX - rect.left;
        this.offsetY = event.clientY - rect.top;

        element.style.cursor = 'grabbing';
        element.style.zIndex = '100';
    },

    startConnection: function(agentId, startX, startY) {
        this.isConnecting = true;
        this.connectFromAgentId = agentId;

        const svg = document.querySelector('.canvas-connections');
        if (svg) {
            this.tempLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
            this.tempLine.setAttribute('x1', startX);
            this.tempLine.setAttribute('y1', startY);
            this.tempLine.setAttribute('x2', startX);
            this.tempLine.setAttribute('y2', startY);
            this.tempLine.setAttribute('stroke', '#0071E3');
            this.tempLine.setAttribute('stroke-width', '2');
            this.tempLine.setAttribute('stroke-dasharray', '5,5');
            this.tempLine.classList.add('temp-connection');
            svg.appendChild(this.tempLine);
        }
    },

    dispose: function() {
        this.dotNetRef = null;
    },

    getContainerRect: function() {
        const container = document.querySelector('.canvas-container');
        if (!container) return null;
        const rect = container.getBoundingClientRect();
        return {
            left: rect.left,
            top: rect.top,
            width: rect.width,
            height: rect.height
        };
    }
};
