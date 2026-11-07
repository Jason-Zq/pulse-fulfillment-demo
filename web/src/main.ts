import { createApp } from 'vue'
import { createPinia } from 'pinia'
import 'element-plus/dist/index.css'
import App from './App.vue'
import router from './router'

// Element Plus 组件由各页面按需显式 import；配合路由级 dynamic import() 做 code-split，
// 不再全量 app.use(ElementPlus)，避免整套组件库被打进主 chunk。
createApp(App).use(createPinia()).use(router).mount('#app')
